using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace TiffTown.Core;

public sealed class SourceImage : IDisposable
{
    public const int ColorCountCap = 32769;

    public string Path { get; }
    public int Width => Pixels.Width;
    public int Height => Pixels.Height;
    public int UniqueColorCount { get; }
    public bool UniqueColorCountCapped => UniqueColorCount >= ColorCountCap;

    internal Image<Rgb24> Pixels { get; }

    private SourceImage(string path, Image<Rgb24> pixels, int uniqueColors)
    {
        Path = path;
        Pixels = pixels;
        UniqueColorCount = uniqueColors;
    }

    public static SourceImage Load(string path)
    {
        using var loaded = Image.Load<Rgba32>(path);

        // Animated inputs contribute their first frame only.
        while (loaded.Frames.Count > 1)
            loaded.Frames.RemoveFrame(1);

        loaded.Mutate(x => x.BackgroundColor(Color.Black));
        var flat = loaded.CloneAs<Rgb24>();
        return new SourceImage(path, flat, CountUniqueColors(flat));
    }

    internal static int CountUniqueColors(Image<Rgb24> image)
    {
        var seen = new HashSet<int>();
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                foreach (var p in accessor.GetRowSpan(y))
                {
                    seen.Add(p.R << 16 | p.G << 8 | p.B);
                    if (seen.Count >= ColorCountCap)
                        return;
                }
            }
        });
        return seen.Count;
    }

    internal Image<Rgb24> CreateScaled(ConversionOptions options)
    {
        if (options.TargetWidth is not int tw || options.TargetHeight is not int th
            || (tw == Width && th == Height))
        {
            return Pixels.Clone();
        }

        IResampler sampler = options.Resample == ResampleMode.Sharp
            ? KnownResamplers.NearestNeighbor
            : KnownResamplers.Bicubic;

        switch (options.Scale)
        {
            case ScaleMode.Stretch:
                return Pixels.Clone(x => x.Resize(tw, th, sampler));

            case ScaleMode.Fit:
                return Pixels.Clone(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(tw, th),
                    Mode = ResizeMode.Pad,
                    Sampler = sampler,
                    PadColor = new Color(options.FillColor),
                }));

            case ScaleMode.Crop:
                return Pixels.Clone(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(tw, th),
                    Mode = ResizeMode.Crop,
                    Sampler = sampler,
                }));

            default:
                {
                    var canvas = new Image<Rgb24>(tw, th, options.FillColor);
                    using var piece = Pixels.Clone();
                    if (piece.Width > tw || piece.Height > th)
                    {
                        int cw = Math.Min(piece.Width, tw);
                        int ch = Math.Min(piece.Height, th);
                        piece.Mutate(x => x.Crop(new Rectangle(
                            (piece.Width - cw) / 2, (piece.Height - ch) / 2, cw, ch)));
                    }
                    var at = new Point((tw - piece.Width) / 2, (th - piece.Height) / 2);
                    canvas.Mutate(x => x.DrawImage(piece, at, 1f));
                    return canvas;
                }
        }
    }

    public void Dispose() => Pixels.Dispose();
}
