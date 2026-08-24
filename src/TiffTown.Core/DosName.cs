using System.IO;
using System.Text;

namespace TiffTown.Core;

public static class DosName
{
    // Derives an 8.3 output name from any source path: uppercase, letters, digits,
    // underscore and hyphen only, eight characters, ".TIF" extension.
    public static string ToTif(string sourcePath)
    {
        string stem = Path.GetFileNameWithoutExtension(sourcePath);
        var sb = new StringBuilder(8);

        foreach (char c in stem.ToUpperInvariant())
        {
            if (sb.Length == 8)
                break;
            if (c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-')
                sb.Append(c);
        }

        return (sb.Length == 0 ? "IMAGE" : sb.ToString()) + ".TIF";
    }
}
