using System.Text;

namespace NtfsRescue.Core.Fat32;

public sealed class Fat32Entry
{
    public string Name = "";
    public string FullPath = "";
    public string Status = "";
    public long Size;
    public DateTime Modified;
    public DateTime Created;
    public bool IsDirectory;
    public bool Deleted;
    public uint FirstCluster;
    public byte Attributes;

    public override string ToString() => FullPath;
}

public sealed class Fat32DirEntry
{
    public byte[] Raw = new byte[32];
    public string ShortName = "";
    public string Name = "";
    public byte Attributes;
    public bool Deleted;
    public bool IsLfn;
    public bool IsVolumeLabel;
    public bool IsDirectory;
    public uint FirstCluster;
    public long Size;
    public DateTime Modified;
    public DateTime Created;

    public int LfnSequence;
    public bool LfnLast;
    public byte LfnChecksum;
    public string LfnPart = "";

    public static Fat32DirEntry Parse(byte[] raw)
    {
        var e = new Fat32DirEntry { Raw = (byte[])raw.Clone() };
        if (raw.Length < 32) return e;

        e.Deleted = raw[0] == 0xE5;
        e.Attributes = raw[0x0B];
        e.IsLfn = e.Attributes == 0x0F;
        e.IsVolumeLabel = (e.Attributes & 0x08) != 0;
        e.IsDirectory = (e.Attributes & 0x10) != 0;
        e.FirstCluster = (uint)(BitConverter.ToUInt16(raw, 0x14) << 16 |
                                BitConverter.ToUInt16(raw, 0x1A));
        e.Size = BitConverter.ToUInt32(raw, 0x1C);
        e.Modified = FatDate(raw, 0x18, 0x16);
        e.Created = FatDate(raw, 0x10, 0x0E);

        if (e.IsLfn)
        {
            int seq = raw[0];
            e.LfnLast = (seq & 0x40) != 0;
            e.LfnSequence = seq & 0x1F;
            e.LfnChecksum = raw[0x0D];
            e.LfnPart = ReadLfnPart(raw);
            return e;
        }

        e.ShortName = ParseShortName(raw);
        e.Name = e.ShortName;
        return e;
    }

    public static bool IsEnd(byte[] raw) => raw.Length >= 1 && raw[0] == 0x00;

    public static byte Checksum(byte[] shortEntry)
    {
        byte sum = 0;
        for (int i = 0; i < 11; i++)
            sum = (byte)(((sum & 1) << 7) + (sum >> 1) + shortEntry[i]);
        return sum;
    }

    public static string BuildLongName(IEnumerable<Fat32DirEntry> parts, byte expectedChecksum)
    {
        var list = parts.ToList();
        if (list.Count == 0) return "";
        if (list.Any(p => p.LfnChecksum != expectedChecksum)) return "";
        var ordered = list.OrderBy(p => p.LfnSequence).ToList();
        var sb = new StringBuilder();
        foreach (var p in ordered) sb.Append(p.LfnPart);
        return sb.ToString().TrimEnd('\0', '\uFFFF').Trim();
    }

    private static string ReadLfnPart(byte[] raw)
    {
        int[] offsets = { 1, 3, 5, 7, 9, 14, 16, 18, 20, 22, 24, 28, 30 };
        var sb = new StringBuilder(13);
        foreach (int o in offsets)
        {
            if (o + 1 >= raw.Length) break;
            char c = (char)BitConverter.ToUInt16(raw, o);
            if (c == '\0' || c == '\uFFFF') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static string ParseShortName(byte[] raw)
    {
        var baseName = new StringBuilder(8);
        for (int i = 0; i < 8; i++)
        {
            byte c = raw[i];
            if (c == 0x20) continue;
            if (i == 0 && c == 0x05) c = 0xE5;
            if (i == 0 && c == 0xE5) c = (byte)'_';
            baseName.Append((char)c);
        }

        var ext = new StringBuilder(3);
        for (int i = 8; i < 11; i++)
        {
            byte c = raw[i];
            if (c == 0x20) continue;
            ext.Append((char)c);
        }

        byte ntRes = raw[0x0C];
        var name = baseName.ToString();
        if ((ntRes & 0x08) != 0) name = name.ToLowerInvariant();
        var extension = ext.ToString();
        if ((ntRes & 0x10) != 0) extension = extension.ToLowerInvariant();

        return extension.Length > 0 ? name + "." + extension : name;
    }

    private static DateTime FatDate(byte[] raw, int dateOffset, int timeOffset)
    {
        if (dateOffset + 2 > raw.Length || timeOffset + 2 > raw.Length) return DateTime.MinValue;
        int date = BitConverter.ToUInt16(raw, dateOffset);
        int time = BitConverter.ToUInt16(raw, timeOffset);
        if (date == 0) return DateTime.MinValue;

        int day = date & 0x1F;
        int month = (date >> 5) & 0x0F;
        int year = 1980 + ((date >> 9) & 0x7F);
        int second = (time & 0x1F) * 2;
        int minute = (time >> 5) & 0x3F;
        int hour = (time >> 11) & 0x1F;

        try { return new DateTime(year, month, day, hour, minute, second); }
        catch { return DateTime.MinValue; }
    }
}
