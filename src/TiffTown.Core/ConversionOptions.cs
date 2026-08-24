using SixLabors.ImageSharp.PixelFormats;

namespace TiffTown.Core;

public enum ColorDepth { Auto, Colors16, Colors256, Colors32K, TrueColor }
public enum ScaleMode { Stretch, Fit, Crop, Center }
public enum ResampleMode { Smooth, Sharp }

public sealed class ConversionOptions
{
    public ColorDepth Depth { get; set; } = ColorDepth.Auto;
    public int? TargetWidth { get; set; }
    public int? TargetHeight { get; set; }
    public ScaleMode Scale { get; set; } = ScaleMode.Stretch;
    public Rgb24 FillColor { get; set; } = new(0, 0, 0);
    public ResampleMode Resample { get; set; } = ResampleMode.Smooth;
    public bool Dither { get; set; } = true;
    public bool Lzw { get; set; }
}
