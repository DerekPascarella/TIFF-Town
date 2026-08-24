using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using SixLabors.ImageSharp.PixelFormats;

namespace TiffTown.Core;

// Emits the fixed TIFF layout written by Fujitsu's TownsOS imaging software:
// IFD at 8, auxiliary values in the 0x100-0x1FF window, pixel data at 0x200,
// except 256-color where the palette sits at 0x200 and data at 0x800.
// See docs/DESIGN.md section 1 for the reverse-engineered template.
internal static class TownsTiffWriter
{
    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;
    private const ushort TypeRational = 5;

    private readonly record struct Entry(ushort Tag, ushort Type, uint Count, uint Value);

    public static byte[] Write16(int width, int height, ReadOnlySpan<Rgb24> palette, byte[] indices, bool lzw)
    {
        int rowBytes = (width + 1) / 2;
        var strip = new byte[rowBytes * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // FillOrder 2, matching Towns VRAM: the left pixel of each byte
                // pair occupies the low nibble.
                int shift = (x & 1) == 0 ? 0 : 4;
                strip[y * rowBytes + x / 2] |= (byte)(indices[y * width + x] << shift);
            }
        }

        var paletteArray = palette.ToArray();
        return Assemble(width, height, dataOffset: 0x200, strip, lzw, header =>
        {
            WriteColorMap(header, 0x100, paletteArray, 16);
        }, entries: (comp, len) => new List<Entry>
        {
            new(254, TypeLong, 1, 0),
            new(256, TypeShort, 1, (uint)width),
            new(257, TypeShort, 1, (uint)height),
            new(258, TypeShort, 1, 4),
            new(259, TypeShort, 1, comp),
            new(262, TypeShort, 1, 3),
            new(266, TypeShort, 1, 2),
            new(273, TypeLong, 1, 0x200),
            new(277, TypeShort, 1, 1),
            new(278, TypeLong, 1, (uint)height),
            new(279, TypeLong, 1, len),
            new(281, TypeShort, 1, 15),
            new(282, TypeRational, 1, 0x1F0),
            new(283, TypeRational, 1, 0x1F8),
            new(284, TypeShort, 1, 1),
            new(320, TypeShort, 48, 0x100),
        });
    }

    public static byte[] Write256(int width, int height, ReadOnlySpan<Rgb24> palette, byte[] indices, bool lzw)
    {
        var paletteArray = palette.ToArray();
        return Assemble(width, height, dataOffset: 0x800, indices, lzw, header =>
        {
            WriteColorMap(header, 0x200, paletteArray, 256);
        }, entries: (comp, len) => new List<Entry>
        {
            new(254, TypeLong, 1, 0),
            new(256, TypeShort, 1, (uint)width),
            new(257, TypeShort, 1, (uint)height),
            new(258, TypeShort, 1, 8),
            new(259, TypeShort, 1, comp),
            new(262, TypeShort, 1, 3),
            new(266, TypeShort, 1, 1),
            new(273, TypeLong, 1, 0x800),
            new(277, TypeShort, 1, 1),
            new(278, TypeLong, 1, (uint)height),
            new(279, TypeLong, 1, len),
            new(281, TypeShort, 1, 255),
            new(282, TypeRational, 1, 0x1F0),
            new(283, TypeRational, 1, 0x1F8),
            new(284, TypeShort, 1, 1),
            new(320, TypeShort, 768, 0x200),
        });
    }

    public static byte[] Write32K(int width, int height, ushort[] grbWords, bool lzw)
    {
        var strip = new byte[grbWords.Length * 2];
        for (int i = 0; i < grbWords.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(strip.AsSpan(i * 2), grbWords[i]);

        return Assemble(width, height, dataOffset: 0x200, strip, lzw, header => { },
            entries: (comp, len) => new List<Entry>
        {
            new(254, TypeLong, 1, 0),
            new(256, TypeShort, 1, (uint)width),
            new(257, TypeShort, 1, (uint)height),
            new(258, TypeShort, 1, 16),
            new(259, TypeShort, 1, comp),
            new(262, TypeShort, 1, 1),
            new(266, TypeShort, 1, 1),
            new(273, TypeLong, 1, 0x200),
            new(277, TypeShort, 1, 1),
            new(278, TypeLong, 1, (uint)height),
            new(279, TypeLong, 1, len),
            new(281, TypeShort, 1, 32767),
            new(282, TypeRational, 1, 0x1F0),
            new(283, TypeRational, 1, 0x1F8),
            new(284, TypeShort, 1, 1),
        });
    }

    public static byte[] WriteTrueColor(int width, int height, ReadOnlySpan<Rgb24> pixels, bool lzw)
    {
        var strip = new byte[pixels.Length * 3];
        for (int i = 0; i < pixels.Length; i++)
        {
            strip[i * 3] = pixels[i].R;
            strip[i * 3 + 1] = pixels[i].G;
            strip[i * 3 + 2] = pixels[i].B;
        }

        return Assemble(width, height, dataOffset: 0x200, strip, lzw, header =>
        {
            for (int i = 0; i < 3; i++)
            {
                PutU16(header, 0x1E4 + i * 2, 8);
                PutU16(header, 0x1EA + i * 2, 255);
            }
        }, entries: (comp, len) => new List<Entry>
        {
            new(254, TypeLong, 1, 0),
            new(256, TypeShort, 1, (uint)width),
            new(257, TypeShort, 1, (uint)height),
            new(258, TypeShort, 3, 0x1E4),
            new(259, TypeShort, 1, comp),
            new(262, TypeShort, 1, 2),
            new(266, TypeShort, 1, 1),
            new(273, TypeLong, 1, 0x200),
            new(277, TypeShort, 1, 3),
            new(278, TypeLong, 1, (uint)height),
            new(279, TypeLong, 1, len),
            new(281, TypeShort, 3, 0x1EA),
            new(282, TypeRational, 1, 0x1F0),
            new(283, TypeRational, 1, 0x1F8),
            new(284, TypeShort, 1, 1),
        });
    }

    private static byte[] Assemble(
        int width, int height, int dataOffset, byte[] rawStrip, bool lzw,
        Action<byte[]> writeAux, Func<uint, uint, List<Entry>> entries)
    {
        byte[] strip = lzw ? TiffLzw.Encode(rawStrip) : rawStrip;
        uint compression = lzw ? 5u : 1u;

        var header = new byte[dataOffset];
        header[0] = (byte)'I';
        header[1] = (byte)'I';
        PutU16(header, 2, 42);
        PutU32(header, 4, 8);

        var list = entries(compression, (uint)strip.Length);
        PutU16(header, 8, (ushort)list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            int at = 10 + i * 12;
            PutU16(header, at, list[i].Tag);
            PutU16(header, at + 2, list[i].Type);
            PutU32(header, at + 4, list[i].Count);
            PutU32(header, at + 8, list[i].Value);
        }

        // XRes and YRes rationals, 75/1, at their fixed slots.
        PutU32(header, 0x1F0, 75);
        PutU32(header, 0x1F4, 1);
        PutU32(header, 0x1F8, 75);
        PutU32(header, 0x1FC, 1);

        writeAux(header);

        var file = new byte[dataOffset + strip.Length];
        header.CopyTo(file, 0);
        strip.CopyTo(file, dataOffset);
        return file;
    }

    private static void WriteColorMap(byte[] header, int offset, ReadOnlySpan<Rgb24> palette, int slots)
    {
        for (int i = 0; i < palette.Length; i++)
        {
            PutU16(header, offset + i * 2, (ushort)(palette[i].R * 257));
            PutU16(header, offset + (slots + i) * 2, (ushort)(palette[i].G * 257));
            PutU16(header, offset + (slots * 2 + i) * 2, (ushort)(palette[i].B * 257));
        }
    }

    private static void PutU16(byte[] buffer, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), value);

    private static void PutU32(byte[] buffer, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), value);
}
