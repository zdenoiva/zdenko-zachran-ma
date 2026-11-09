using System.Text;
using NtfsRescue.Util;

namespace NtfsRescue.Core;

public sealed class NtfsBootSector
{
    public byte[] Raw = Array.Empty<byte>();
    public bool IsValid;
    public string Error = "";

    public int BytesPerSector;
    public int SectorsPerCluster;
    public int ClusterSize;
    public long TotalSectors;
    public long VolumeSize;
    public long MftLcn;
    public long MftMirrLcn;
    public int MftRecordSize;
    public int IndexRecordSize;
    public long Serial;

    public long TotalClusters => SectorsPerCluster > 0 ? TotalSectors / SectorsPerCluster : 0;

    public static NtfsBootSector Parse(byte[] raw)
    {
        var b = new NtfsBootSector { Raw = raw };
        if (raw.Length < 512)
        {
            b.Error = "boot sector je príliš krátky";
            return b;
        }

        if (raw[3] != (byte)'N' || raw[4] != (byte)'T' || raw[5] != (byte)'F' ||
            raw[6] != (byte)'S' || raw[7] != (byte)' ' || raw[8] != (byte)' ' ||
            raw[9] != (byte)' ' || raw[10] != (byte)' ')
        {
            b.Error = "chýba signatúra NTFS";
            return b;
        }

        if (Bin.U16(raw, 510) != 0xAA55)
        {
            b.Error = "chýba značka 0xAA55";
            return b;
        }

        int bytesPerSector = Bin.U16(raw, 0x0B);
        int sectorsPerCluster = raw[0x0D];
        if (bytesPerSector is not (512 or 1024 or 2048 or 4096))
        {
            b.Error = $"neplatná veľkosť sektora ({bytesPerSector})";
            return b;
        }
        if (sectorsPerCluster == 0 || (sectorsPerCluster & (sectorsPerCluster - 1)) != 0 || sectorsPerCluster > 256)
        {
            b.Error = $"neplatný počet sektorov v klasteri ({sectorsPerCluster})";
            return b;
        }

        int clusterSize = bytesPerSector * sectorsPerCluster;
        if (clusterSize > 2 * 1024 * 1024)
        {
            b.Error = $"neplatná veľkosť klastra ({clusterSize})";
            return b;
        }

        long totalSectors = Bin.I64(raw, 0x28);
        long mftLcn = Bin.I64(raw, 0x30);
        long mftMirrLcn = Bin.I64(raw, 0x38);
        sbyte recordClusters = (sbyte)raw[0x40];
        sbyte indexClusters = (sbyte)raw[0x44];

        int recordSize = recordClusters < 0
            ? 1 << (-recordClusters)
            : recordClusters * clusterSize;
        int indexSize = indexClusters < 0
            ? 1 << (-indexClusters)
            : indexClusters * clusterSize;

        if (recordSize < 512 || recordSize > 65536 || (recordSize & (recordSize - 1)) != 0)
        {
            b.Error = $"neplatná veľkosť MFT záznamu ({recordSize})";
            return b;
        }
        if (indexSize <= 0 || indexSize > 65536) indexSize = 4096;

        long totalClusters = totalSectors / sectorsPerCluster;
        if (totalSectors <= 0 || totalClusters <= 0)
        {
            b.Error = "neplatný počet sektorov";
            return b;
        }
        if (mftLcn <= 0 || mftLcn >= totalClusters)
        {
            b.Error = $"neplatná poloha $MFT ({mftLcn})";
            return b;
        }
        if (mftMirrLcn <= 0 || mftMirrLcn >= totalClusters)
        {
            b.Error = $"neplatná poloha $MFTMirr ({mftMirrLcn})";
            return b;
        }

        b.BytesPerSector = bytesPerSector;
        b.SectorsPerCluster = sectorsPerCluster;
        b.ClusterSize = clusterSize;
        b.TotalSectors = totalSectors;
        b.VolumeSize = totalSectors * bytesPerSector;
        b.MftLcn = mftLcn;
        b.MftMirrLcn = mftMirrLcn;
        b.MftRecordSize = recordSize;
        b.IndexRecordSize = indexSize;
        b.Serial = (long)Bin.U64(raw, 0x48);
        b.IsValid = true;
        return b;
    }

    public string Describe() =>
        $"NTFS: {Fmt.Bytes(VolumeSize)}, sektor {BytesPerSector} B, klaster {Fmt.Bytes(ClusterSize)}, " +
        $"$MFT LCN {MftLcn}, $MFTMirr LCN {MftMirrLcn}, záznam {MftRecordSize} B, sériové číslo {Serial:X}";

    public string Compare(NtfsBootSector other)
    {
        var diffs = new List<string>();
        void Check(string name, long a, long c)
        {
            if (a != c) diffs.Add($"{name}: {a} != {c}");
        }
        Check("bytes/sector", BytesPerSector, other.BytesPerSector);
        Check("sectors/cluster", SectorsPerCluster, other.SectorsPerCluster);
        Check("total sectors", TotalSectors, other.TotalSectors);
        Check("MFT LCN", MftLcn, other.MftLcn);
        Check("MFTMirr LCN", MftMirrLcn, other.MftMirrLcn);
        Check("record size", MftRecordSize, other.MftRecordSize);
        Check("serial", Serial, other.Serial);
        return diffs.Count == 0 ? "rovnaké" : string.Join("; ", diffs);
    }
}

internal static class Bin
{
    public static ushort U16(byte[] b, int o) => BitConverter.ToUInt16(b, o);
    public static uint U32(byte[] b, int o) => BitConverter.ToUInt32(b, o);
    public static ulong U64(byte[] b, int o) => BitConverter.ToUInt64(b, o);
    public static long I64(byte[] b, int o) => BitConverter.ToInt64(b, o);
    public static int I32(byte[] b, int o) => BitConverter.ToInt32(b, o);

    public static void W16(byte[] b, int o, ushort v) => BitConverter.GetBytes(v).CopyTo(b, o);
    public static void W32(byte[] b, int o, uint v) => BitConverter.GetBytes(v).CopyTo(b, o);
    public static void W64(byte[] b, int o, ulong v) => BitConverter.GetBytes(v).CopyTo(b, o);

    public static string Ascii(byte[] b, int o, int len)
    {
        var sb = new StringBuilder(len);
        for (int i = 0; i < len && o + i < b.Length; i++)
        {
            var c = b[o + i];
            if (c == 0) break;
            sb.Append((char)c);
        }
        return sb.ToString();
    }
}
