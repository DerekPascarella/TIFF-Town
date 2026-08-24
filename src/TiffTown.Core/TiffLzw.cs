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
            // so a decoder lagging one code behind never needs a wider code
            // than it has already switched to.
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
}
