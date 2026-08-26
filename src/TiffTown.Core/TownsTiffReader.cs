using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TiffTown.Core;

// Reads the FM Towns TIFF variants written by TownsTiffWriter and by the
// TownsOS imaging software. Not a general TIFF reader: identifies a known
// variant from its tag values and rejects anything else.
internal static class TownsTiffReader
{
    private readonly record struct Entry(ushort Tag, ushort Type, uint Count, uint Value);

    public readonly record struct Result(TownsTiffMode Mode, bool Lzw, Image<Rgb24> Pixels);

    private const string NotRecognized = "Not a recognized FM Towns TIFF layout.";

    public static Result Read(byte[] bytes)
    {
        if (bytes.Length < 10 || bytes[0] != (byte)'I' || bytes[1] != (byte)'I'
            || BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)) != 42)
            throw new InvalidDataException(NotRecognized);

        int ifd = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));
        if (ifd < 0 || ifd + 2 > bytes.Length)
            throw new InvalidDataException(NotRecognized);

        int count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(ifd));
        if (ifd + 2 + count * 12 > bytes.Length)
            throw new InvalidDataException(NotRecognized);

        var entries = new Dictionary<ushort, Entry>();
        for (int i = 0; i < count; i++)
        {
            var e = bytes.AsSpan(ifd + 2 + i * 12);
            var entry = new Entry(
                BinaryPrimitives.ReadUInt16LittleEndian(e),
                BinaryPrimitives.ReadUInt16LittleEndian(e[2..]),
                BinaryPrimitives.ReadUInt32LittleEndian(e[4..]),
                BinaryPrimitives.ReadUInt32LittleEndian(e[8..]));
            entries[entry.Tag] = entry;
        }

        if (!entries.TryGetValue(256, out var widthE) || !entries.TryGetValue(257, out var heightE)
            || !entries.TryGetValue(258, out var bpsE) || !entries.TryGetValue(277, out var sppE)
            || !entries.TryGetValue(262, out var photoE) || !entries.TryGetValue(273, out var stripE)
            || !entries.TryGetValue(259, out var compE))
            throw new InvalidDataException(NotRecognized);

        int width = (int)widthE.Value;
        int height = (int)heightE.Value;
        if (width <= 0 || height <= 0)
            throw new InvalidDataException(NotRecognized);

        bool lzw = compE.Value switch
        {
            1 => false,
            5 => true,
            _ => throw new InvalidDataException(NotRecognized),
        };

        int stripOffset = (int)stripE.Value;
        if (stripOffset < 0 || stripOffset > bytes.Length)
            throw new InvalidDataException(NotRecognized);

        // Every variant has one strip that runs to the end of the file, so
        // StripByteCounts is ignored. The TownsOS 256-color writer fills it
        // with garbage.
        byte[] rawStrip = bytes.AsSpan(stripOffset).ToArray();
        byte[] strip = lzw ? TiffLzw.Decode(rawStrip) : rawStrip;

        ushort fillOrder = entries.TryGetValue(266, out var foE) ? (ushort)foE.Value : (ushort)1;
        int samplesPerPixel = (int)sppE.Value;
        int photometric = (int)photoE.Value;

        try
        {
            if (bpsE.Count == 1 && bpsE.Value == 4 && samplesPerPixel == 1 && fillOrder == 2 && photometric == 3)
                return new Result(TownsTiffMode.Colors16, lzw, Decode16(width, height, strip, ReadPalette(bytes, entries, 16)));

            // 16-level grayscale: same 4bpp FillOrder=2 packing but BlackIsZero
            // and no ColorMap. TownsTiffWriter never emits this, but some
            // TownsOS art uses it.
            if (bpsE.Count == 1 && bpsE.Value == 4 && samplesPerPixel == 1 && fillOrder == 2 && photometric == 1)
                return new Result(TownsTiffMode.Colors16, lzw, Decode16Gray(width, height, strip));

            if (bpsE.Count == 1 && bpsE.Value == 8 && samplesPerPixel == 1 && photometric == 3)
                return new Result(TownsTiffMode.Colors256, lzw, Decode256(width, height, strip, ReadPalette(bytes, entries, 256)));

            if (bpsE.Count == 1 && bpsE.Value == 16 && samplesPerPixel == 1 && photometric == 1)
                return new Result(TownsTiffMode.Colors32K, lzw, Decode32K(width, height, strip));

            if (bpsE.Count == 3 && samplesPerPixel == 3 && photometric == 2)
                return new Result(TownsTiffMode.TrueColor, lzw, DecodeTrueColor(width, height, strip));
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            // Strip too short for the declared dimensions, palette offset
            // out of range, and similar shape mismatches all land here.
            throw new InvalidDataException(NotRecognized, ex);
        }

        throw new InvalidDataException(NotRecognized);
    }

    private static Rgb24[] ReadPalette(byte[] bytes, Dictionary<ushort, Entry> entries, int slots)
    {
        if (!entries.TryGetValue(320, out var cm))
            throw new InvalidDataException(NotRecognized);

        int offset = (int)cm.Value;
        var palette = new Rgb24[slots];
        for (int i = 0; i < slots; i++)
        {
            byte r = (byte)(ReadU16(bytes, offset + i * 2) / 257);
            byte g = (byte)(ReadU16(bytes, offset + (slots + i) * 2) / 257);
            byte b = (byte)(ReadU16(bytes, offset + (slots * 2 + i) * 2) / 257);
            palette[i] = new Rgb24(r, g, b);
        }
        return palette;
    }

    private static ushort ReadU16(byte[] bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset));

    private static Image<Rgb24> Decode16(int width, int height, byte[] strip, Rgb24[] palette)
    {
        int rowBytes = (width + 1) / 2;
        var image = new Image<Rgb24>(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                // FillOrder 2, matching Towns VRAM: the left pixel of each
                // byte pair occupies the low nibble.
                byte b = strip[y * rowBytes + x / 2];
                int index = (x & 1) == 0 ? (b & 0x0F) : (b >> 4);
                image[x, y] = palette[index];
            }
        return image;
    }

    private static Image<Rgb24> Decode16Gray(int width, int height, byte[] strip)
    {
        int rowBytes = (width + 1) / 2;
        var image = new Image<Rgb24>(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                byte b = strip[y * rowBytes + x / 2];
                int nibble = (x & 1) == 0 ? (b & 0x0F) : (b >> 4);
                byte gray = (byte)(nibble * 17);
                image[x, y] = new Rgb24(gray, gray, gray);
            }
        return image;
    }

    private static Image<Rgb24> Decode256(int width, int height, byte[] strip, Rgb24[] palette)
    {
        var image = new Image<Rgb24>(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                image[x, y] = palette[strip[y * width + x]];
        return image;
    }

    private static Image<Rgb24> Decode32K(int width, int height, byte[] strip)
    {
        var image = new Image<Rgb24>(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                ushort word = ReadU16(strip, (y * width + x) * 2);
                int g5 = (word >> 10) & 0x1F;
                int r5 = (word >> 5) & 0x1F;
                int b5 = word & 0x1F;
                image[x, y] = new Rgb24(Converter.Expand5(r5), Converter.Expand5(g5), Converter.Expand5(b5));
            }
        return image;
    }

    private static Image<Rgb24> DecodeTrueColor(int width, int height, byte[] strip)
    {
        var image = new Image<Rgb24>(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 3;
                image[x, y] = new Rgb24(strip[i], strip[i + 1], strip[i + 2]);
            }
        return image;
    }
}
