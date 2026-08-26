using System;
using System.Linq;
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
using MsBox.Avalonia.Dto;
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
    private TownsTiffImage? _viewerImage;
    private WriteableBitmap? _viewerBitmap;
    private TownsHdImage? _hdImage;
    private WriteableBitmap? _installBitmap;
    private byte[]? _installBytes;
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
        ViewerLoadButton.Click += async (_, _) => await ViewerPickAndLoadAsync();
        ViewerSaveButton.Click += async (_, _) => await ViewerSaveAsync();
        InstallLoadButton.Click += async (_, _) => await InstallPickAndLoadAsync();
        InstallTifButton.Click += async (_, _) => await InstallPickTifAsync();
        InstallButton.Click += async (_, _) => await InstallAsync();
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
        MainTabs.SelectionChanged += (_, _) => OnTabChanged();

        Opened += (_, _) =>
        {
            if (Program.StartupFile != null)
                _ = LoadFileAsync(Program.StartupFile);
        };

        OnTabChanged();
    }

    private void VersionLine_PointerPressed(object? sender, PointerPressedEventArgs e) =>
        new AboutWindow().ShowDialog(this);

    // The preview pane and compare button are shared: the Viewer and Install
    // tabs show their own loaded TIFF, the other tabs the conversion output.
    private void OnTabChanged()
    {
        if (MainTabs.SelectedIndex == 2)
        {
            PreviewImage.Source = _viewerBitmap;
            PreviewHint.IsVisible = _viewerImage == null;
            CompareButton.IsEnabled = false;
        }
        else if (MainTabs.SelectedIndex == 3)
        {
            PreviewImage.Source = _installBitmap;
            PreviewHint.IsVisible = _installBitmap == null;
            CompareButton.IsEnabled = false;
        }
        else
        {
            PreviewImage.Source = _previewBitmap ?? _originalBitmap;
            PreviewHint.IsVisible = _source == null;
            CompareButton.IsEnabled = _result != null;
        }
        OnOptionChanged();
    }

    private void OnOptionChanged()
    {
        if (MainTabs.SelectedIndex is 2 or 3)
            return;

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
                    _ = ShowError("Could not convert the image.", ex);
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

    private static WriteableBitmap ToBitmap(SixLabors.ImageSharp.Image<Rgb24> image)
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
                    byte* dest = (byte*)fb.Address + y * fb.RowBytes;
                    for (int x = 0; x < row.Length; x++)
                    {
                        dest[x * 4] = row[x].R;
                        dest[x * 4 + 1] = row[x].G;
                        dest[x * 4 + 2] = row[x].B;
                        dest[x * 4 + 3] = 255;
                    }
                }
            }
        });
        return bmp;
    }

    private static bool IsTiffPath(string path) =>
        path.EndsWith(".tif", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase);

    private async Task ShowError(string headline, Exception ex, string? extraNote = null)
    {
        string body = extraNote == null
            ? $"{headline}\n\nDetails: {ex.Message}"
            : $"{headline}\n\n{extraNote}\n\nDetails: {ex.Message}";
        await MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
        {
            ContentTitle = "TIFF Town",
            ContentMessage = body,
            Icon = MsBox.Avalonia.Enums.Icon.None,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            MaxWidth = 440,
        }).ShowWindowDialogAsync(this);
    }

    private async Task ShowInfo(string message)
    {
        await MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
        {
            ContentTitle = "TIFF Town",
            ContentMessage = message,
            Icon = MsBox.Avalonia.Enums.Icon.None,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            MaxWidth = 440,
        }).ShowWindowDialogAsync(this);
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
            await ShowError("Could not open the file picker.", ex);
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
            string? note = IsTiffPath(path)
                ? "If this is already an FM Towns TIFF, open it in the Viewer tab to inspect it instead."
                : null;
            await ShowError($"Could not read {System.IO.Path.GetFileName(path)} as a source image.", ex, note);
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
            await ShowInfo($"Saved {file.TryGetLocalPath() ?? file.Name} ({_result.TiffBytes.Length:N0} bytes).");
        }
        catch (Exception ex)
        {
            await ShowError("Could not save the file.", ex);
        }
    }

    private async Task ViewerPickAndLoadAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load Towns TIFF",
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Towns TIFF") { Patterns = new[] { "*.tif", "*.tiff" } },
                    FilePickerFileTypes.All,
                },
            });
            if (files.Count == 1 && files[0].TryGetLocalPath() is string path)
                await ViewerLoadFileAsync(path);
        }
        catch (Exception ex)
        {
            await ShowError("Could not open the file picker.", ex);
        }
    }

    private async Task ViewerLoadFileAsync(string path)
    {
        try
        {
            var loaded = await Task.Run(() => TownsTiffImage.Load(path));
            _viewerImage?.Dispose();
            _viewerImage = loaded;

            var oldBitmap = _viewerBitmap;
            _viewerBitmap = ToBitmap(loaded.Pixels);
            PreviewImage.Source = _viewerBitmap;
            PreviewHint.IsVisible = false;
            oldBitmap?.Dispose();

            long fileSize = new System.IO.FileInfo(path).Length;
            ViewerInfo.Text = $"{System.IO.Path.GetFileName(path)}, {loaded.Width}x{loaded.Height}\n"
                + $"{ModeLabel(loaded.Mode)}, {(loaded.Lzw ? "LZW" : "Uncompressed")}\n"
                + $"{fileSize:N0} bytes";
            ViewerSaveButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ViewerSaveButton.IsEnabled = false;
            await ShowError($"Could not read {System.IO.Path.GetFileName(path)}.", ex);
        }
    }

    private async Task ViewerSaveAsync()
    {
        if (_viewerImage == null)
            return;
        try
        {
            var startDir = System.IO.Path.GetDirectoryName(_viewerImage.Path) is string dir
                ? await StorageProvider.TryGetFolderFromPathAsync(dir) : null;
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save as PNG",
                SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(_viewerImage.Path) + ".png",
                DefaultExtension = "png",
                SuggestedStartLocation = startDir,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("PNG") { Patterns = new[] { "*.png" } },
                },
            });
            if (file == null)
                return;
            using var ms = new System.IO.MemoryStream();
            await SixLabors.ImageSharp.ImageExtensions.SaveAsPngAsync(_viewerImage.Pixels, ms);
            byte[] bytes = ms.ToArray();
            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(bytes);
            await ShowInfo($"Saved {file.TryGetLocalPath() ?? file.Name} ({bytes.Length:N0} bytes).");
        }
        catch (Exception ex)
        {
            await ShowError("Could not save the file.", ex);
        }
    }

    private void RefreshInstallState() =>
        InstallButton.IsEnabled = _installBytes != null && _hdImage != null
            && _hdImage.Partitions.Any(p => p.IsTownsSystem);

    private async Task InstallPickAndLoadAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load HDD Image",
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("HDD images")
                    {
                        Patterns = new[] { "*.hda", "*.hds", "*.hdd", "*.img", "*.hd", "*.hdn",
                            "*.hdf", "*.h0", "*.h1", "*.h2", "*.h3", "*.h4", "*.h5",
                            "*.nhd", "*.hdi", "*.vhd", "*.bin" },
                    },
                    FilePickerFileTypes.All,
                },
            });
            if (files.Count == 1 && files[0].TryGetLocalPath() is string path)
                await InstallLoadFileAsync(path);
        }
        catch (Exception ex)
        {
            await ShowError("Could not open the file picker.", ex);
        }
    }

    private async Task InstallLoadFileAsync(string path)
    {
        try
        {
            var image = await Task.Run(() => TownsHdImage.Survey(path));
            _hdImage = image;
            string container = char.ToUpperInvariant(image.Container[0]) + image.Container[1..];
            InstallImageInfo.Text = $"{System.IO.Path.GetFileName(path)}\n"
                + $"{container}, {image.Partitions.Count} partition{(image.Partitions.Count == 1 ? "" : "s")}";
            var targets = image.Partitions.Where(p => p.IsTownsSystem).ToList();
            InstallTargetInfo.Text = targets.Count == 0
                ? "No TownsOS system partition found."
                : string.Join("\n", targets.Select(p =>
                    $"Partition {p.Index + 1}: {(p.Boot ? "bootable " : "")}TownsOS system"
                    + (p.HasWallpaper ? ", TMENU.TIF present" : "")));
        }
        catch (Exception ex)
        {
            _hdImage = null;
            InstallImageInfo.Text = "N/A";
            InstallTargetInfo.Text = "N/A";
            await ShowError($"Could not read {System.IO.Path.GetFileName(path)} as a Towns HDD image.", ex);
        }
        RefreshInstallState();
    }

    private async Task InstallPickTifAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load Wallpaper TIFF",
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Towns TIFF") { Patterns = new[] { "*.tif", "*.tiff" } },
                    FilePickerFileTypes.All,
                },
            });
            if (files.Count == 1 && files[0].TryGetLocalPath() is string path)
                await InstallLoadTifAsync(path);
        }
        catch (Exception ex)
        {
            await ShowError("Could not open the file picker.", ex);
        }
    }

    private async Task InstallLoadTifAsync(string path)
    {
        try
        {
            var (loaded, bytes) = await Task.Run(() =>
            {
                var image = TownsTiffImage.Load(path);
                bool legal = (image.Width == 640 && image.Height == 480 && image.Mode == TownsTiffMode.Colors16)
                    || (image.Width == 320 && image.Height == 240 && image.Mode == TownsTiffMode.Colors32K);
                if (!legal)
                {
                    string shape = $"{image.Width}x{image.Height} at {ModeLabel(image.Mode)}";
                    image.Dispose();
                    throw new System.IO.InvalidDataException("TownsOS only displays 640x480 16-color or "
                        + $"320x240 32,768-color wallpapers; this file is {shape}.");
                }
                return (image, System.IO.File.ReadAllBytes(path));
            });

            var oldBitmap = _installBitmap;
            _installBitmap = ToBitmap(loaded.Pixels);
            _installBytes = bytes;
            if (MainTabs.SelectedIndex == 3)
            {
                PreviewImage.Source = _installBitmap;
                PreviewHint.IsVisible = false;
            }
            oldBitmap?.Dispose();
            InstallTifInfo.Text = $"{System.IO.Path.GetFileName(path)}, {loaded.Width}x{loaded.Height}\n"
                + $"{ModeLabel(loaded.Mode)}, {bytes.Length:N0} bytes";
            loaded.Dispose();
        }
        catch (Exception ex)
        {
            await ShowError($"Could not load {System.IO.Path.GetFileName(path)} as a wallpaper TIFF.", ex,
                "Use the Wallpaper tab to convert a regular image into one.");
        }
        RefreshInstallState();
    }

    private async Task InstallAsync()
    {
        if (_installBytes == null || _hdImage == null)
            return;
        var image = _hdImage;
        var payload = _installBytes;
        var targets = image.Partitions.Where(p => p.IsTownsSystem).ToList();
        string fileName = System.IO.Path.GetFileName(image.Path);
        string where = targets.Count == 1
            ? $"partition {targets[0].Index + 1}"
            : "partitions " + string.Join(", ", targets.Select(p => p.Index + 1));

        var confirm = await MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
        {
            ContentTitle = "TIFF Town",
            ContentMessage = $"Install the wallpaper into {fileName}?\n\n"
                + $"TMENU.TIF will be written to the root of {where}, "
                + "replacing any TMENU.TIF already there.",
            ButtonDefinitions = MsBox.Avalonia.Enums.ButtonEnum.YesNo,
            Icon = MsBox.Avalonia.Enums.Icon.None,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            MaxWidth = 440,
        }).ShowWindowDialogAsync(this);
        if (confirm != MsBox.Avalonia.Enums.ButtonResult.Yes)
            return;

        InstallButton.IsEnabled = false;
        try
        {
            var installed = await Task.Run(() => image.InstallWallpaper(payload));
            string list = installed.Count == 1
                ? $"partition {installed[0].Index + 1}"
                : "partitions " + string.Join(" and ", installed.Select(p => p.Index + 1));
            await ShowInfo($"Installed TMENU.TIF into {list} of {fileName} "
                + $"({payload.Length:N0} bytes, verified).");
            await InstallLoadFileAsync(image.Path);
        }
        catch (Exception ex)
        {
            await ShowError("Could not install the wallpaper.", ex);
            RefreshInstallState();
        }
    }
}
