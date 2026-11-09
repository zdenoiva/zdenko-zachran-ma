using NtfsRescue.Util;

namespace NtfsRescue.Core;

public sealed class Fat32BootSector
{
    public byte[] Raw = Array.Empty<byte>();
    public bool IsValid;
    public string Error = "";

    public int BytesPerSector;
    public int SectorsPerCluster;
    public int ClusterSize;
    public int ReservedSectors;
    public int FatCount;
    public int RootEntryCount;
    public long TotalSectors;
    public int FatSize16;
    public long FatSize32;
    public long HiddenSectors;
    public long RootCluster;
    public int FsInfoSector;
    public int BackupBootSector;
    public uint VolumeSerial;
    public string VolumeLabel = "";
    public string FsType = "";

    public long VolumeSize => TotalSectors * BytesPerSector;
    public long FatStartOffset => (long)ReservedSectors * BytesPerSector;
    public long FatSizeBytes => FatSize32 * BytesPerSector;
    public long DataStartOffset => (long)(ReservedSectors + FatCount * FatSize32) * BytesPerSector;
    public long DataSectors => TotalSectors - (ReservedSectors + (long)FatCount * FatSize32);
    public long ClusterCount => SectorsPerCluster > 0 ? DataSectors / SectorsPerCluster : 0;
    public int RootClusterInt => RootCluster is > 0 and <= int.MaxValue ? (int)RootCluster : -1;

    public static Fat32BootSector Parse(byte[] raw)
    {
        var b = new Fat32BootSector { Raw = raw };
        if (raw.Length < 512)
        {
            b.Error = "boot sector je príliš krátky";
            return b;
        }

        if (raw[510] != 0x55 || raw[511] != 0xAA)
        {
            b.Error = "chýba značka 0x55AA";
            return b;
        }

        b.BytesPerSector = BitConverter.ToUInt16(raw, 0x0B);
        b.SectorsPerCluster = raw[0x0D];
        b.ReservedSectors = BitConverter.ToUInt16(raw, 0x0E);
        b.FatCount = raw[0x10];
        b.RootEntryCount = BitConverter.ToUInt16(raw, 0x11);
        long total16 = BitConverter.ToUInt16(raw, 0x13);
        b.FatSize16 = BitConverter.ToUInt16(raw, 0x16);
        long total32 = BitConverter.ToUInt32(raw, 0x20);
        b.FatSize32 = BitConverter.ToUInt32(raw, 0x24);
        b.RootCluster = BitConverter.ToUInt32(raw, 0x2C);
        b.FsInfoSector = BitConverter.ToUInt16(raw, 0x30);
        b.BackupBootSector = BitConverter.ToUInt16(raw, 0x32);
        b.VolumeSerial = BitConverter.ToUInt32(raw, 0x43);
        b.VolumeLabel = EncodingASCII(raw, 0x47, 11).Trim();
        b.FsType = EncodingASCII(raw, 0x52, 8).Trim();

        if (b.BytesPerSector is not (512 or 1024 or 2048 or 4096))
        {
            b.Error = $"neplatná veľkosť sektora ({b.BytesPerSector})";
            return b;
        }
        if (b.SectorsPerCluster == 0 || (b.SectorsPerCluster & (b.SectorsPerCluster - 1)) != 0 ||
            b.SectorsPerCluster > 128)
        {
            b.Error = $"neplatný počet sektorov v klastri ({b.SectorsPerCluster})";
            return b;
        }
        if (b.ReservedSectors <= 0)
        {
            b.Error = "neplatný počet rezervovaných sektorov";
            return b;
        }
        if (b.FatCount <= 0 || b.FatCount > 2)
        {
            b.Error = $"neplatný počet FAT ({b.FatCount})";
            return b;
        }
        if (b.FatSize32 <= 0)
        {
            b.Error = "chýba 32-bitová veľkosť FAT";
            return b;
        }
        if (b.RootEntryCount != 0)
        {
            b.Error = "root entry count nie je 0 (nie je FAT32)";
            return b;
        }

        b.TotalSectors = total16 != 0 ? total16 : total32;
        b.ClusterSize = b.BytesPerSector * b.SectorsPerCluster;
        b.HiddenSectors = BitConverter.ToUInt32(raw, 0x1C);

        if (b.TotalSectors <= 0)
        {
            b.Error = "neplatný počet sektorov";
            return b;
        }
        if (b.RootCluster < 2 || b.RootCluster >= b.ClusterCount + 2)
        {
            b.Error = $"neplatný root cluster ({b.RootCluster})";
            return b;
        }
        if (b.ClusterSize <= 0 || b.ClusterSize > 1024 * 1024)
        {
            b.Error = $"neplatná veľkosť klastra ({b.ClusterSize})";
            return b;
        }
        if (b.ClusterCount <= 0)
        {
            b.Error = "neplatný počet klastrov";
            return b;
        }

        // FAT32 sa pozná aj podľa typu, ale primárne rozhodujú BPB polia.
        if (!b.FsType.Equals("FAT32", StringComparison.OrdinalIgnoreCase) &&
            b.ClusterCount < 65525)
        {
            b.Error = "BPB nevyzerá ako FAT32";
            return b;
        }

        b.IsValid = true;
        return b;
    }

    public string Describe() =>
        $"FAT32: {Fmt.Bytes(VolumeSize)}, sektor {BytesPerSector} B, klaster {Fmt.Bytes(ClusterSize)}, " +
        $"FAT {FatCount}× {Fmt.Bytes(FatSizeBytes)}, root cluster {RootCluster}, " +
        $"záloha boot {BackupBootSector}, serial {VolumeSerial:X8}";

    private static string EncodingASCII(byte[] b, int offset, int length)
    {
        var sb = new System.Text.StringBuilder(length);
        for (int i = 0; i < length && offset + i < b.Length; i++)
        {
            byte c = b[offset + i];
            if (c == 0) break;
            sb.Append((char)c);
        }
        return sb.ToString();
    }
}
