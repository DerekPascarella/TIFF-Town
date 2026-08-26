using System;
using System.Collections.Generic;
using System.IO;

namespace TiffTown.Core;

// TIFF-variant LZW (TIFF 6.0 section 13): MSB-first bit packing, ClearCode 256,
// EOI 257, early code-width change, table reset at 4094 entries.
internal static class TiffLzw
{
    private const int ClearCode = 256;
    private const int EoiCode = 257;

    public static byte[] Encode(byte[] data)
    {
        var output = new MemoryStream();
        int bitBuffer = 0;
        int bitCount = 0;
        int codeWidth = 9;
        var table = new Dictionary<(int Prefix, byte Next), int>();
        int nextCode = 258;

        void WriteCode(int code)
        {
            bitBuffer = (bitBuffer << codeWidth) | code;
            bitCount += codeWidth;
            while (bitCount >= 8)
            {
                output.WriteByte((byte)(bitBuffer >> (bitCount - 8)));
                bitCount -= 8;
                bitBuffer &= (1 << bitCount) - 1;
            }
        }

        void Reset()
        {
            table.Clear();
            nextCode = 258;
            codeWidth = 9;
        }

        WriteCode(ClearCode);
        Reset();

        int prefix = -1;
        foreach (byte b in data)
        {
            if (prefix < 0)
            {
                prefix = b;
                continue;
            }

            if (table.TryGetValue((prefix, b), out int found))
            {
                prefix = found;
                continue;
            }

            WriteCode(prefix);
            table[(prefix, b)] = nextCode++;

            // Early change: widen as soon as the table reaches 2^width entries,
            // one code sooner than plain LZW.
            if (nextCode == 512 || nextCode == 1024 || nextCode == 2048)
                codeWidth++;

            if (nextCode == 4094)
            {
                WriteCode(ClearCode);
                Reset();
            }

            prefix = b;
        }

        if (prefix >= 0)
            WriteCode(prefix);
        WriteCode(EoiCode);

        if (bitCount > 0)
            output.WriteByte((byte)(bitBuffer << (8 - bitCount)));

        return output.ToArray();
    }

    public static byte[] Decode(byte[] data)
    {
        var output = new MemoryStream();
        int bitPos = 0;
        int totalBits = data.Length * 8;
        int codeWidth = 9;
        List<byte[]> table = NewTable();
        byte[]? prev = null;

        int ReadCode()
        {
            int code = 0;
            for (int i = 0; i < codeWidth; i++)
            {
                int bit = (data[bitPos >> 3] >> (7 - (bitPos & 7))) & 1;
                code = (code << 1) | bit;
                bitPos++;
            }
            return code;
        }

        while (bitPos + codeWidth <= totalBits)
        {
            int code = ReadCode();
            if (code == EoiCode)
                break;
            if (code == ClearCode)
            {
                table = NewTable();
                codeWidth = 9;
                prev = null;
                continue;
            }

            byte[] entry;
            if (code < table.Count)
                entry = table[code];
            else if (code == table.Count && prev != null)
                entry = Append(prev, prev[0]);
            else
                throw new InvalidDataException("Corrupt LZW stream.");

            output.Write(entry, 0, entry.Length);

            if (prev != null)
            {
                table.Add(Append(prev, entry[0]));

                // The decoder's table trails the encoder's by one entry, so the
                // early width change lands at 511/1023/2047 here.
                if (table.Count == 511 || table.Count == 1023 || table.Count == 2047)
                    codeWidth++;
            }
            prev = entry;
        }

        return output.ToArray();
    }

    private static List<byte[]> NewTable()
    {
        var table = new List<byte[]>(4096);
        for (int i = 0; i < 256; i++)
            table.Add(new[] { (byte)i });
        table.Add(Array.Empty<byte>()); // 256: ClearCode, never indexed.
        table.Add(Array.Empty<byte>()); // 257: EoiCode, never indexed.
        return table;
    }

    private static byte[] Append(byte[] seq, byte b)
    {
        var result = new byte[seq.Length + 1];
        seq.CopyTo(result, 0);
        result[seq.Length] = b;
        return result;
    }
}
