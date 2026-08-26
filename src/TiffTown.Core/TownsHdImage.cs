using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TiffTown.Core;

// One entry from the partition table of a Towns hard-disk image.
public sealed class TownsHdPartition
{
    public int Index { get; init; }
    public bool Boot { get; init; }
    public byte Type { get; init; }
    public string TypeName { get; init; } = "";
    public long StartBlock { get; init; }
    public long BlockCount { get; init; }
    public string Label { get; init; } = "";
    public bool IsTownsSystem { get; internal set; }
    public bool HasWallpaper { get; internal set; }
    public string? Note { get; internal set; }
}

// Reads the partition table of an FM Towns SCSI hard-disk image and writes
// TMENU.TIF into the root directory of its TownsOS system partition(s).
// Accepts raw images regardless of extension, fixed VHD, T98-Next NHD, and
// Anex86 HDI containers.
public sealed class TownsHdImage
{
    public string Path { get; }
    public long BaseOffset { get; }
    public int BlockSize { get; }
    public string Container { get; }
    public IReadOnlyList<TownsHdPartition> Partitions { get; }

    // Block 1 header signatures: "Fujitsu" and "Matsushita C" in Shift-JIS.
    private static readonly byte[][] TableSigs =
    {
        new byte[] { 0x95, 0x78, 0x8E, 0x6D, 0x92, 0xCA },
        new byte[] { 0x8F, 0xBC, 0x89, 0xBA, 0x82, 0x62 },
    };

    private static readonly Dictionary<byte, string> TypeNames = new()
    {
        [0x01] = "MS-DOS",
        [0x04] = "MS-DOS EXT",
        [0x06] = "MS-DOS 512",
        [0x10] = "XENIX",
        [0x12] = "APCS",
        [0x15] = "NETWARE",
        [0x20] = "Linux",
        [0x21] = "Linux swap",
        [0x90] = "OASYS",
    };

    private static readonly byte[][] SystemMarkers =
    {
        Encoding.ASCII.GetBytes("TMENU   EXP"),
        Encoding.ASCII.GetBytes("TOWNS   SYS"),
        Encoding.ASCII.GetBytes("TBIOS   SYS"),
    };

    private static readonly byte[] WallpaperName = Encoding.ASCII.GetBytes("TMENU   TIF");

    private TownsHdImage(string path, long baseOffset, int blockSize, string container,
        IReadOnlyList<TownsHdPartition> partitions)
    {
        Path = path;
        BaseOffset = baseOffset;
        BlockSize = blockSize;
        Container = container;
        Partitions = partitions;
    }

    public static TownsHdImage Survey(string path)
    {
        using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var (baseOffset, block, container, parts) = FindDisk(f);
        foreach (var p in parts)
        {
            if (!IsFatType(p.Type))
            {
                p.Note = "not a FAT partition";
                continue;
            }
            try
            {
                var fat = new FatVolume(f, baseOffset + p.StartBlock * block);
                p.IsTownsSystem = fat.RootHasAny(SystemMarkers);
                p.HasWallpaper = fat.RootHasAny(new[] { WallpaperName });
            }
            catch (InvalidDataException ex)
            {
                p.Note = ex.Message;
            }
        }
        return new TownsHdImage(path, baseOffset, block, container, parts);
    }

    // Writes the wallpaper into every TownsOS system partition, then reads each
    // copy back and compares it byte for byte. Returns the partitions written.
    public IReadOnlyList<TownsHdPartition> InstallWallpaper(byte[] tiff)
    {
        using var f = new FileStream(Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var (baseOffset, block, _, parts) = FindDisk(f);
        var installed = new List<TownsHdPartition>();
        foreach (var p in parts)
        {
            if (!IsFatType(p.Type))
                continue;
            FatVolume fat;
            try
            {
                fat = new FatVolume(f, baseOffset + p.StartBlock * block);
            }
            catch (InvalidDataException)
            {
                continue;
            }
            if (!fat.RootHasAny(SystemMarkers))
                continue;
            fat.Put(WallpaperName, tiff);
            var back = new FatVolume(f, baseOffset + p.StartBlock * block).Get(WallpaperName);
            if (!back.AsSpan().SequenceEqual(tiff))
                throw new IOException($"Verification failed on partition {p.Index + 1}: "
                    + "the file read back does not match what was written.");
            p.HasWallpaper = true;
            installed.Add(p);
        }
        if (installed.Count == 0)
            throw new InvalidDataException("No TownsOS system partition was found on this image.");
        return installed;
    }

    private static bool IsFatType(byte type) => type is 0x01 or 0x04 or 0x06;

    private static (long, int, string, List<TownsHdPartition>) FindDisk(FileStream f)
    {
        int[] blockSizes = { 512, 256, 1024, 2048 };

        var known = ContainerBase(f);
        if (known is var (knownBase, knownNote) && knownNote != null)
        {
            foreach (int block in blockSizes)
            {
                var parts = ParseTable(f, knownBase, block);
                if (parts != null)
                    return (knownBase, block, knownNote, parts);
            }
        }

        // An unrecognized fixed-size header is skipped by scanning the first
        // 64 KB for the table.
        for (long baseOffset = 0; baseOffset <= 0x10000; baseOffset += 0x100)
        {
            foreach (int block in blockSizes)
            {
                var parts = ParseTable(f, baseOffset, block);
                if (parts == null)
                    continue;
                string note = baseOffset == 0 ? "raw image" : $"image with a {baseOffset}-byte header";
                if (baseOffset == 0 && f.Length >= 512)
                {
                    var tail = ReadAt(f, f.Length - 512, 8);
                    if (tail.AsSpan().SequenceEqual("conectix"u8))
                        note = "fixed VHD image";
                }
                return (baseOffset, block, note, parts);
            }
        }
        throw new InvalidDataException("No Towns partition table was found. "
            + "This does not look like an FM Towns hard-disk image.");
    }

    private static (long, string?) ContainerBase(FileStream f)
    {
        if (f.Length < 512)
            return (0, null);
        var head = ReadAt(f, 0, 512);
        if (head.AsSpan(0, 15).SequenceEqual("T98HDDIMAGE.R0\0"u8))
            return (ReadU32(head, 0x110), "T98-Next NHD image");
        if (head.AsSpan(0, 8).SequenceEqual("conectix"u8))
            throw new InvalidDataException("Dynamic VHD images are not supported. "
                + "Convert it to a fixed VHD or a raw image first.");
        uint headerSize = ReadU32(head, 0x08);
        uint dataSize = ReadU32(head, 0x0C);
        uint sectorSize = ReadU32(head, 0x10);
        if (sectorSize is 256 or 512 && headerSize >= 32 && headerSize <= 0x100000
            && headerSize + (long)dataSize == f.Length)
            return (headerSize, "Anex86 HDI image");
        return (0, null);
    }

    // The table fills block 1: a 32-byte header whose word at 0x0E records the
    // bytes per block, then ten 48-byte entries. An empty entry is detected by
    // its type byte alone, because its text fields are space padded.
    private static List<TownsHdPartition>? ParseTable(FileStream f, long baseOffset, int block)
    {
        if (baseOffset + 2L * block > f.Length)
            return null;
        var s0 = ReadAt(f, baseOffset, 4);
        var s1 = ReadAt(f, baseOffset + block, 512);

        bool hasIpl = s0.AsSpan().SequenceEqual("IPL4"u8);
        bool hasSig = false;
        foreach (var sig in TableSigs)
            hasSig |= s1.AsSpan(0, 6).SequenceEqual(sig);
        if (!hasIpl && !hasSig)
            return null;
        if (ReadU16(s1, 0x0E) != block)
            return null;

        var parts = new List<TownsHdPartition>();
        for (int i = 0; i < 10; i++)
        {
            int at = 0x20 + i * 0x30;
            byte type = s1[at + 1];
            if (type == 0)
                continue;
            long start = ReadU32(s1, at + 2);
            long count = ReadU32(s1, at + 6);
            if (start < 3 || count == 0 || baseOffset + (start + count) * block > f.Length)
                return null;
            parts.Add(new TownsHdPartition
            {
                Index = i,
                Boot = s1[at] == 0xFF,
                Type = type,
                TypeName = TypeNames.TryGetValue(type, out var name) ? name : $"type {type:X2}",
                StartBlock = start,
                BlockCount = count,
                Label = AsciiLabel(s1, at + 0x20, 16),
            });
        }
        return parts.Count > 0 ? parts : null;
    }

    private static string AsciiLabel(byte[] data, int at, int length)
    {
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
            sb.Append(data[at + i] is >= 0x20 and < 0x7F ? (char)data[at + i] : '?');
        return sb.ToString().TrimEnd();
    }

    private static byte[] ReadAt(FileStream f, long offset, int count)
    {
        var buffer = new byte[count];
        f.Seek(offset, SeekOrigin.Begin);
        f.ReadExactly(buffer);
        return buffer;
    }

    private static ushort ReadU16(byte[] b, int at) => (ushort)(b[at] | b[at + 1] << 8);

    private static uint ReadU32(byte[] b, int at) =>
        (uint)(b[at] | b[at + 1] << 8 | b[at + 2] << 16 | b[at + 3] << 24);

    // Root-directory read and write access to one FAT12 or FAT16 partition.
    private sealed class FatVolume
    {
        private const byte Deleted = 0xE5;

        private readonly FileStream _f;
        private readonly long _base;
        private readonly int _sector;
        private readonly int _spc;
        private readonly int _fatCount;
        private readonly int _rootSlots;
        private readonly int _fatBytes;
        private readonly long _fatAt;
        private readonly long _rootAt;
        private readonly long _dataAt;
        private readonly int _clusters;
        private readonly int _clusterBytes;
        private readonly bool _fat12;

        public FatVolume(FileStream f, long baseOffset)
        {
            _f = f;
            _base = baseOffset;
            if (baseOffset + 64 > f.Length)
                throw new InvalidDataException("The image ends before the partition data.");
            var b = ReadAt(f, baseOffset, 64);

            _sector = ReadU16(b, 0x0B);
            _spc = b[0x0D];
            int reserved = ReadU16(b, 0x0E);
            _fatCount = b[0x10];
            _rootSlots = ReadU16(b, 0x11);
            int fatSectors = ReadU16(b, 0x16);
            long total = ReadU16(b, 0x13);
            if (total == 0)
                total = ReadU32(b, 0x20);

            if (_sector is not (512 or 1024 or 2048 or 4096)
                || _spc is not (1 or 2 or 4 or 8 or 16 or 32 or 64 or 128)
                || _fatCount is not (1 or 2)
                || reserved == 0 || _rootSlots == 0 || fatSectors == 0 || total == 0)
                throw new InvalidDataException("The partition has no FAT boot sector.");

            _clusterBytes = _sector * _spc;
            _fatAt = (long)reserved * _sector;
            _fatBytes = fatSectors * _sector;
            long rootSector = reserved + (long)_fatCount * fatSectors;
            _rootAt = rootSector * _sector;
            long rootSectors = ((long)_rootSlots * 32 + _sector - 1) / _sector;
            long dataSector = rootSector + rootSectors;
            _dataAt = dataSector * _sector;
            _clusters = (int)((total - dataSector) / _spc);

            if (_clusters >= 65525)
                throw new InvalidDataException("FAT32 is not used on Towns partitions.");
            _fat12 = _clusters < 4085;
            long fatNeed = _fat12 ? ((long)_clusters + 2) * 3 / 2 : ((long)_clusters + 2) * 2;
            if (fatNeed > _fatBytes)
                throw new InvalidDataException("The FAT is too small for the cluster count.");
            if (baseOffset + total * _sector > f.Length)
                throw new InvalidDataException("The partition claims more sectors than the image holds.");
        }

        private int EndOfChain => _fat12 ? 0xFFF : 0xFFFF;
        private int BadCluster => _fat12 ? 0xFF7 : 0xFFF7;

        public bool RootHasAny(IReadOnlyList<byte[]> names)
        {
            var raw = ReadAt(_f, _base + _rootAt, _rootSlots * 32);
            for (int i = 0; i < _rootSlots; i++)
            {
                var entry = raw.AsSpan(i * 32, 32);
                if (entry[0] == 0)
                    break;
                if (entry[0] == Deleted || (entry[11] & 0x18) != 0)
                    continue;
                foreach (var name in names)
                    if (entry.Slice(0, 11).SequenceEqual(name))
                        return true;
            }
            return false;
        }

        public void Put(byte[] name, byte[] data)
        {
            var fat = ReadAt(_f, _base + _fatAt, _fatBytes);
            var (index, entry) = FindSlot(name);
            var oldChain = entry == null ? new List<int>() : Chain(fat, ReadU16(entry, 26));

            // The new clusters are written before the old chain is freed, so an
            // interrupted run leaves the existing file intact.
            int need = (data.Length + _clusterBytes - 1) / _clusterBytes;
            var fresh = Allocate(fat, need);
            for (int i = 0; i < fresh.Count; i++)
            {
                int chunk = Math.Min(_clusterBytes, data.Length - i * _clusterBytes);
                _f.Seek(_base + _dataAt + (long)(fresh[i] - 2) * _clusterBytes, SeekOrigin.Begin);
                _f.Write(data, i * _clusterBytes, chunk);
            }

            foreach (int n in oldChain)
                FatSet(fat, n, 0);
            for (int i = 0; i < fresh.Count; i++)
                FatSet(fat, fresh[i], i + 1 < fresh.Count ? fresh[i + 1] : EndOfChain);
            for (int i = 0; i < _fatCount; i++)
            {
                _f.Seek(_base + _fatAt + (long)i * _fatBytes, SeekOrigin.Begin);
                _f.Write(fat, 0, _fatBytes);
            }

            var stamp = DateTime.Now;
            ushort date = (ushort)((stamp.Year - 1980 << 9) | (stamp.Month << 5) | stamp.Day);
            ushort time = (ushort)((stamp.Hour << 11) | (stamp.Minute << 5) | (stamp.Second / 2));
            var slot = new byte[32];
            entry?.CopyTo(slot, 0);
            if (entry == null)
            {
                name.CopyTo(slot, 0);
                slot[11] = 0x20;
                WriteU16(slot, 14, time);
                WriteU16(slot, 16, date);
            }
            WriteU16(slot, 18, date);
            WriteU16(slot, 20, 0);
            WriteU16(slot, 22, time);
            WriteU16(slot, 24, date);
            WriteU16(slot, 26, (ushort)(fresh.Count > 0 ? fresh[0] : 0));
            WriteU32(slot, 28, (uint)data.Length);
            _f.Seek(_base + _rootAt + index * 32L, SeekOrigin.Begin);
            _f.Write(slot, 0, 32);
            _f.Flush();
        }

        public byte[] Get(byte[] name)
        {
            var fat = ReadAt(_f, _base + _fatAt, _fatBytes);
            var (_, entry) = FindSlot(name);
            if (entry == null)
                throw new InvalidDataException("The file is not in the root directory.");
            uint size = ReadU32(entry, 28);
            var output = new MemoryStream();
            foreach (int n in Chain(fat, ReadU16(entry, 26)))
            {
                output.Write(ReadAt(_f, _base + _dataAt + (long)(n - 2) * _clusterBytes, _clusterBytes));
                if (output.Length >= size)
                    break;
            }
            if (output.Length < size)
                throw new InvalidDataException("The file's cluster chain is shorter than its size.");
            var result = output.ToArray();
            Array.Resize(ref result, (int)size);
            return result;
        }

        private (int, byte[]?) FindSlot(byte[] name)
        {
            var raw = ReadAt(_f, _base + _rootAt, _rootSlots * 32);
            int free = -1;
            for (int i = 0; i < _rootSlots; i++)
            {
                var entry = raw.AsSpan(i * 32, 32);
                if (entry[0] == 0)
                    return (free >= 0 ? free : i, null);
                if (entry[0] == Deleted)
                    free = free >= 0 ? free : i;
                else if ((entry[11] & 0x08) == 0 && entry.Slice(0, 11).SequenceEqual(name))
                    return (i, entry.ToArray());
            }
            if (free < 0)
                throw new InvalidDataException("The root directory of the partition is full.");
            return (free, null);
        }

        private List<int> Chain(byte[] fat, int first)
        {
            var chain = new List<int>();
            int n = first;
            while (n >= 2 && n < BadCluster && chain.Count <= _clusters)
            {
                chain.Add(n);
                n = FatGet(fat, n);
            }
            return chain;
        }

        private List<int> Allocate(byte[] fat, int count)
        {
            var found = new List<int>();
            for (int n = 2; n < _clusters + 2 && found.Count < count; n++)
                if (FatGet(fat, n) == 0)
                    found.Add(n);
            if (found.Count < count)
                throw new InvalidDataException($"Not enough free space on the partition: "
                    + $"{count} clusters needed, {found.Count} available.");
            return found;
        }

        private int FatGet(byte[] fat, int n)
        {
            if (!_fat12)
                return ReadU16(fat, n * 2);
            int i = n * 3 / 2;
            int v = fat[i] | fat[i + 1] << 8;
            return (n & 1) != 0 ? v >> 4 : v & 0xFFF;
        }

        private void FatSet(byte[] fat, int n, int value)
        {
            if (!_fat12)
            {
                WriteU16(fat, n * 2, (ushort)value);
                return;
            }
            int i = n * 3 / 2;
            if ((n & 1) != 0)
            {
                fat[i] = (byte)((fat[i] & 0x0F) | (value << 4 & 0xF0));
                fat[i + 1] = (byte)(value >> 4);
            }
            else
            {
                fat[i] = (byte)value;
                fat[i + 1] = (byte)((fat[i + 1] & 0xF0) | (value >> 8 & 0x0F));
            }
        }

        private static void WriteU16(byte[] b, int at, ushort value)
        {
            b[at] = (byte)value;
            b[at + 1] = (byte)(value >> 8);
        }

        private static void WriteU32(byte[] b, int at, uint value)
        {
            b[at] = (byte)value;
            b[at + 1] = (byte)(value >> 8);
            b[at + 2] = (byte)(value >> 16);
            b[at + 3] = (byte)(value >> 24);
        }
    }
}
