using System;
using System.Collections.Generic;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;

namespace TiffTown.Core;

public sealed class ConversionResult : IDisposable
{
    public TownsTiffMode Mode { get; }
    public int Width { get; }
    public int Height { get; }
    public byte[] TiffBytes { get; }
    public Image<Rgba32> Preview { get; }

    internal ConversionResult(TownsTiffMode mode, int width, int height, byte[] tiff, Image<Rgba32> preview)
    {
        Mode = mode;
        Width = width;
        Height = height;
        TiffBytes = tiff;
        Preview = preview;
    }

    public void Dispose() => Preview.Dispose();
}

public static class Converter
{
    public static ConversionResult Convert(SourceImage source, ConversionOptions options)
    {
        using var scaled = source.CreateScaled(options);

        TownsTiffMode mode = options.Depth switch
        {
            ColorDepth.Colors16 => TownsTiffMode.Colors16,
            ColorDepth.Colors256 => TownsTiffMode.Colors256,
            ColorDepth.Colors32K => TownsTiffMode.Colors32K,
            ColorDepth.TrueColor => TownsTiffMode.TrueColor,
            _ => AutoMode(scaled),
        };

        return mode switch
        {
            TownsTiffMode.Colors16 => Paletted(scaled, 16, options),
            TownsTiffMode.Colors256 => Paletted(scaled, 256, options),
            TownsTiffMode.Colors32K => To32K(scaled, options),
            _ => ToTrueColor(scaled, options),
        };
    }

    private static TownsTiffMode AutoMode(Image<Rgb24> image)
    {
        int colors = SourceImage.CountUniqueColors(image);
        if (colors <= 16)
            return TownsTiffMode.Colors16;
        if (colors <= 256)
            return TownsTiffMode.Colors256;
        return TownsTiffMode.Colors32K;
    }

    private static ConversionResult Paletted(Image<Rgb24> image, int maxColors, ConversionOptions options)
    {
        Rgb24[] palette;
        byte[] indices = new byte[image.Width * image.Height];

        var exact = TryExactPalette(image, maxColors, indices);
        if (exact != null)
        {
            palette = maxColors == 16 ? SnapExactTo16(exact, indices) : exact;
        }
        else
        {
            var quantizer = new WuQuantizer(new QuantizerOptions
            {
                MaxColors = maxColors,
                Dither = options.Dither ? KnownDitherings.FloydSteinberg : null,
            });
            using IQuantizer<Rgb24> q = quantizer.CreatePixelSpecificQuantizer<Rgb24>(image.Configuration);
            using IndexedImageFrame<Rgb24> frame =
                q.BuildPaletteAndQuantizeFrame(image.Frames.RootFrame, image.Bounds);

            palette = frame.Palette.ToArray();
            for (int y = 0; y < image.Height; y++)
                frame.DangerousGetRowSpan(y).CopyTo(indices.AsSpan(y * image.Width, image.Width));

            if (maxColors == 16)
                palette = SnapQuantizedTo16(image, palette, indices, options);
        }

        byte[] tiff = maxColors == 16
            ? TownsTiffWriter.Write16(image.Width, image.Height, palette, indices, options.Lzw)
            : TownsTiffWriter.Write256(image.Width, image.Height, palette, indices, options.Lzw);

        var preview = new Image<Rgba32>(image.Width, image.Height);
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                var c = palette[indices[y * image.Width + x]];
                preview[x, y] = new Rgba32(c.R, c.G, c.B, 255);
            }

        var mode = maxColors == 16 ? TownsTiffMode.Colors16 : TownsTiffMode.Colors256;
        return new ConversionResult(mode, image.Width, image.Height, tiff, preview);
    }

    // Builds a palette in first-appearance order, or returns null if the image
    // has more colors than slots. Images that fit are never quantized.
    private static Rgb24[]? TryExactPalette(Image<Rgb24> image, int maxColors, byte[] indices)
    {
        var lookup = new Dictionary<Rgb24, byte>();
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
            {
                var c = image[x, y];
                if (!lookup.TryGetValue(c, out byte idx))
                {
                    if (lookup.Count == maxColors)
                        return null;
                    idx = (byte)lookup.Count;
                    lookup[c] = idx;
                }
                indices[y * image.Width + x] = idx;
            }

        var palette = new Rgb24[lookup.Count];
        foreach (var (color, idx) in lookup)
            palette[idx] = color;
        return palette;
    }

    // A 16-color Towns palette has four bits per component. Snapping to that
    // grid gives the color the machine will actually display.
    private static Rgb24 Snap16(Rgb24 c) =>
        new(Expand4(c.R >> 4), Expand4(c.G >> 4), Expand4(c.B >> 4));

    // Snaps an exact palette to the 4-bit grid. Entries that collapse onto the
    // same color are merged and their pixels remapped.
    private static Rgb24[] SnapExactTo16(Rgb24[] palette, byte[] indices)
    {
        var kept = new List<Rgb24>();
        var moved = new byte[palette.Length];
        for (int i = 0; i < palette.Length; i++)
        {
            var snapped = Snap16(palette[i]);
            int at = kept.IndexOf(snapped);
            if (at < 0)
            {
                at = kept.Count;
                kept.Add(snapped);
            }
            moved[i] = (byte)at;
        }

        for (int i = 0; i < indices.Length; i++)
            indices[i] = moved[indices[i]];
        return kept.ToArray();
    }

    // Requantizes the image against the snapped palette. Dithering against
    // colors the machine cannot show leaves gradients banded once the low
    // nibble is dropped.
    private static Rgb24[] SnapQuantizedTo16(
        Image<Rgb24> image, Rgb24[] palette, byte[] indices, ConversionOptions options)
    {
        var kept = new List<Rgb24>();
        bool onGrid = true;
        foreach (var color in palette)
        {
            var snapped = Snap16(color);
            onGrid &= snapped.Equals(color);
            if (!kept.Contains(snapped))
                kept.Add(snapped);
        }
        if (onGrid)
            return palette;

        var colors = new Color[kept.Count];
        for (int i = 0; i < kept.Count; i++)
            colors[i] = Color.FromPixel(kept[i]);

        var quantizer = new PaletteQuantizer(colors, new QuantizerOptions
        {
            MaxColors = kept.Count,
            Dither = options.Dither ? KnownDitherings.FloydSteinberg : null,
        });
        using IQuantizer<Rgb24> q = quantizer.CreatePixelSpecificQuantizer<Rgb24>(image.Configuration);
        using IndexedImageFrame<Rgb24> frame =
            q.BuildPaletteAndQuantizeFrame(image.Frames.RootFrame, image.Bounds);

        for (int y = 0; y < image.Height; y++)
            frame.DangerousGetRowSpan(y).CopyTo(indices.AsSpan(y * image.Width, image.Width));
        return frame.Palette.ToArray();
    }

    private static ConversionResult To32K(Image<Rgb24> image, ConversionOptions options)
    {
        int w = image.Width, h = image.Height;
        var words = new ushort[w * h];
        var preview = new Image<Rgba32>(w, h);

        // Floyd-Steinberg error diffusion per channel, quantizing 8-bit to 5-bit.
        float[] errR = new float[w + 2], errG = new float[w + 2], errB = new float[w + 2];
        for (int y = 0; y < h; y++)
        {
            float[] nextR = new float[w + 2], nextG = new float[w + 2], nextB = new float[w + 2];
            for (int x = 0; x < w; x++)
            {
                var c = image[x, y];
                float r = c.R, g = c.G, b = c.B;
                if (options.Dither)
                {
                    r += errR[x + 1];
                    g += errG[x + 1];
                    b += errB[x + 1];
                }

                int r5 = Math.Clamp((int)(r * 31f / 255f + 0.5f), 0, 31);
                int g5 = Math.Clamp((int)(g * 31f / 255f + 0.5f), 0, 31);
                int b5 = Math.Clamp((int)(b * 31f / 255f + 0.5f), 0, 31);
                words[y * w + x] = (ushort)(g5 << 10 | r5 << 5 | b5);

                byte pr = Expand5(r5), pg = Expand5(g5), pb = Expand5(b5);
                preview[x, y] = new Rgba32(pr, pg, pb, 255);

                if (options.Dither)
                {
                    Diffuse(errR, nextR, x, r - pr);
                    Diffuse(errG, nextG, x, g - pg);
                    Diffuse(errB, nextB, x, b - pb);
                }
            }
            errR = nextR;
            errG = nextG;
            errB = nextB;
        }

        byte[] tiff = TownsTiffWriter.Write32K(w, h, words, options.Lzw);
        return new ConversionResult(TownsTiffMode.Colors32K, w, h, tiff, preview);
    }

    private static void Diffuse(float[] current, float[] next, int x, float error)
    {
        current[x + 2] += error * 7f / 16f;
        next[x] += error * 3f / 16f;
        next[x + 1] += error * 5f / 16f;
        next[x + 2] += error * 1f / 16f;
    }

    internal static byte Expand4(int v) => (byte)(v << 4 | v);

    internal static byte Expand5(int v) => (byte)(v << 3 | v >> 2);

    private static ConversionResult ToTrueColor(Image<Rgb24> image, ConversionOptions options)
    {
        int w = image.Width, h = image.Height;
        var pixels = new Rgb24[w * h];
        var preview = new Image<Rgba32>(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = image[x, y];
                pixels[y * w + x] = c;
                preview[x, y] = new Rgba32(c.R, c.G, c.B, 255);
            }

        byte[] tiff = TownsTiffWriter.WriteTrueColor(w, h, pixels, options.Lzw);
        return new ConversionResult(TownsTiffMode.TrueColor, w, h, tiff, preview);
    }
}
