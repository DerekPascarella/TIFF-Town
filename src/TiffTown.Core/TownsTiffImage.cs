using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TiffTown.Core;

public sealed class TownsTiffImage : IDisposable
{
    public string Path { get; }
    public TownsTiffMode Mode { get; }
    public bool Lzw { get; }
    public int Width => Pixels.Width;
    public int Height => Pixels.Height;

    internal Image<Rgb24> Pixels { get; }

    private TownsTiffImage(string path, TownsTiffMode mode, bool lzw, Image<Rgb24> pixels)
    {
        Path = path;
        Mode = mode;
        Lzw = lzw;
        Pixels = pixels;
    }

    public static TownsTiffImage Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var result = TownsTiffReader.Read(bytes);
        return new TownsTiffImage(path, result.Mode, result.Lzw, result.Pixels);
    }

    public void Dispose() => Pixels.Dispose();
}
