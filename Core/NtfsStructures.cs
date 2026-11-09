using System.Text;

namespace NtfsRescue.Core;

public readonly record struct NtfsRun(long Vcn, long Lcn, long Clusters)
{
    public bool IsSparse => Lcn < 0;
    public long EndVcn => Vcn + Clusters;
    public override string ToString() => IsSparse ? $"sparse VCN {Vcn}+{Clusters}" : $"LCN {Lcn} VCN {Vcn}+{Clusters}";
}

public static class NtfsRunlist
{
    public static List<NtfsRun> Parse(byte[] data, int offset, int maxLength)
    {
        var runs = new List<NtfsRun>();
        long vcn = 0;
        long lcn = 0;
        int pos = offset;
        int end = Math.Min(data.Length, offset + Math.Max(0, maxLength));

        while (pos < end)
        {
            byte header = data[pos++];
            if (header == 0) break;

            int lengthSize = header & 0x0F;
            int offsetSize = (header >> 4) & 0x0F;

            if (lengthSize is 0 or > 8 || offsetSize > 8) break;
            if (pos + lengthSize + offsetSize > end) break;

            long clusters = ReadUnsigned(data, pos, lengthSize);
            pos += lengthSize;

            long delta = 0;
            bool sparse = offsetSize == 0;
            if (!sparse)
            {
                delta = ReadSigned(data, pos, offsetSize);
                pos += offsetSize;
            }

            if (clusters <= 0) break;

            if (sparse)
            {
                runs.Add(new NtfsRun(vcn, -1, clusters));
            }
            else
            {
                lcn += delta;
                if (lcn < 0) break;
                runs.Add(new NtfsRun(vcn, lcn, clusters));
            }

            vcn += clusters;
        }

        return runs;
    }

    private static long ReadUnsigned(byte[] data, int offset, int size)
    {
        if (size == 8)
        {
            var u = BitConverter.ToUInt64(data, offset);
            if (u > long.MaxValue) throw new InvalidDataException("runlist je príliš veľký");
            return (long)u;
        }

        long value = 0;
        for (int i = 0; i < size; i++)
            value |= (long)data[offset + i] << (8 * i);
        return value;
    }

    private static long ReadSigned(byte[] data, int offset, int size)
    {
        if (size == 8) return BitConverter.ToInt64(data, offset);

        long value = 0;
        for (int i = 0; i < size; i++)
            value |= (long)data[offset + i] << (8 * i);

        if ((data[offset + size - 1] & 0x80) != 0)
            value |= -1L << (8 * size);

        return value;
    }

    public static long FindRunIndex(List<NtfsRun> runs, long vcn)
    {
        int lo = 0, hi = runs.Count - 1, found = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            var r = runs[mid];
            if (vcn < r.Vcn) hi = mid - 1;
            else if (vcn >= r.EndVcn) lo = mid + 1;
            else { found = mid; break; }
        }
        return found;
    }
}

public static class AttrType
{
    public const uint StandardInformation = 0x10;
    public const uint AttributeList = 0x20;
    public const uint FileName = 0x30;
    public const uint ObjectId = 0x40;
    public const uint SecurityDescriptor = 0x50;
    public const uint VolumeName = 0x60;
    public const uint VolumeInformation = 0x70;
    public const uint Data = 0x80;
    public const uint IndexRoot = 0x90;
    public const uint IndexAllocation = 0xA0;
    public const uint Bitmap = 0xB0;
    public const uint ReparsePoint = 0xC0;
    public const uint EaInformation = 0xD0;
    public const uint Ea = 0xE0;
    public const uint LoggedUtilityStream = 0x100;
    public const uint End = 0xFFFFFFFF;
}

public sealed class NtfsAttribute
{
    public uint Type;
    public bool NonResident;
    public string Name = "";
    public ushort AttributeId;
    public ushort Flags;
    public int RecordOffset;
    public int Length;

    public byte[]? ResidentValue;
    public long LowestVcn;
    public long HighestVcn;
    public long AllocatedSize;
    public long RealSize;
    public long InitializedSize;
    public ushort CompressionUnit;
    public List<NtfsRun> Runs = new();

    public static NtfsAttribute? TryParse(byte[] record, int offset, int end)
    {
        if (offset + 4 > end) return null;
        uint type = Bin.U32(record, offset);
        if (type == AttrType.End) return null;

        if (offset + 8 > end) return null;
        int length = Bin.I32(record, offset + 4);
        if (length < 24 || length % 8 != 0 || offset + length > end) return null;

        var a = new NtfsAttribute
        {
            Type = type,
            RecordOffset = offset,
            Length = length,
            NonResident = record[offset + 8] != 0,
            AttributeId = Bin.U16(record, offset + 14),
            Flags = Bin.U16(record, offset + 12)
        };

        int nameLength = record[offset + 9];
        int nameOffset = Bin.U16(record, offset + 10);
        if (nameLength > 0 && nameOffset > 0 && nameOffset + nameLength * 2 <= length)
        {
            a.Name = Encoding.Unicode.GetString(record, offset + nameOffset, nameLength * 2);
        }

        if (!a.NonResident)
        {
            if (offset + 24 > end) return null;
            int valueLength = Bin.I32(record, offset + 16);
            int valueOffset = Bin.U16(record, offset + 20);
            if (valueLength < 0 || valueOffset < 0 || valueOffset + valueLength > length) return null;

            a.ResidentValue = new byte[valueLength];
            Buffer.BlockCopy(record, offset + valueOffset, a.ResidentValue, 0, valueLength);
            a.RealSize = valueLength;
            a.AllocatedSize = valueLength;
            a.LowestVcn = 0;
            a.HighestVcn = 0;
            return a;
        }

        if (offset + 64 > end) return null;
        a.LowestVcn = Bin.I64(record, offset + 16);
        a.HighestVcn = Bin.I64(record, offset + 24);
        int runOffset = Bin.U16(record, offset + 32);
        a.CompressionUnit = Bin.U16(record, offset + 34);
        a.AllocatedSize = Bin.I64(record, offset + 40);
        a.RealSize = Bin.I64(record, offset + 48);
        a.InitializedSize = Bin.I64(record, offset + 56);

        if (runOffset <= 0 || runOffset >= length) return null;
        a.Runs = NtfsRunlist.Parse(record, offset + runOffset, length - runOffset);
        return a;
    }
}

public sealed class NtfsRecord
{
    public ulong Number;
    public uint MftRecordNumber;
    public ushort Sequence;
    public ushort LinkCount;
    public bool InUse;
    public bool IsDirectory;
    public ulong BaseRecord;
    public uint UsedSize;
    public List<NtfsAttribute> Attributes = new();
    public byte[] Data = Array.Empty<byte>();

    public static NtfsRecord? TryParse(byte[] data, ulong number, int sectorSize, out string? error)
    {
        error = null;
        if (data.Length < 512 || data[0] != (byte)'F' || data[1] != (byte)'I' ||
            data[2] != (byte)'L' || data[3] != (byte)'E')
        {
            error = "chýba signatúra FILE";
            return null;
        }

        if (sectorSize <= 0 || data.Length % sectorSize != 0)
        {
            error = "veľkosť záznamu nie je násobkom sektora";
            return null;
        }

        int usaOffset = Bin.U16(data, 4);
        int usaCount = Bin.U16(data, 6);
        int sectors = data.Length / sectorSize;

        if (usaCount != sectors + 1 || usaOffset < 0x2A || usaOffset + usaCount * 2 > data.Length)
        {
            error = "neplatná update sequence array";
            return null;
        }

        ushort usn = Bin.U16(data, usaOffset);
        var originals = new ushort[sectors];
        for (int i = 0; i < sectors; i++)
            originals[i] = Bin.U16(data, usaOffset + 2 + i * 2);

        for (int i = 0; i < sectors; i++)
        {
            int p = (i + 1) * sectorSize - 2;
            if (Bin.U16(data, p) != usn)
            {
                error = $"nesúlad fixup v sektore {i}";
                return null;
            }
        }

        for (int i = 0; i < sectors; i++)
        {
            int p = (i + 1) * sectorSize - 2;
            Bin.W16(data, p, originals[i]);
        }

        var rec = new NtfsRecord
        {
            Number = number,
            Data = data,
            Sequence = Bin.U16(data, 0x10),
            MftRecordNumber = data.Length >= 0x30 ? Bin.U32(data, 0x2C) : 0,
            LinkCount = Bin.U16(data, 0x12),
            UsedSize = Bin.U32(data, 0x18),
            BaseRecord = Bin.U64(data, 0x20),
            InUse = (Bin.U16(data, 0x16) & 0x01) != 0,
            IsDirectory = (Bin.U16(data, 0x16) & 0x02) != 0
        };

        int attrOffset = Bin.U16(data, 0x14);
        int end = rec.UsedSize > 0 && rec.UsedSize <= data.Length ? (int)rec.UsedSize : data.Length;
        if (attrOffset < 0x2A || attrOffset >= end)
        {
            error = "neplatný začiatok atribútov";
            return null;
        }

        int pos = attrOffset;
        while (pos + 4 <= end)
        {
            uint type = Bin.U32(data, pos);
            if (type == AttrType.End) break;

            var attr = NtfsAttribute.TryParse(data, pos, end);
            if (attr is null)
            {
                error = $"neplatný atribút na 0x{pos:X}";
                break;
            }
            rec.Attributes.Add(attr);
            pos += attr.Length;
        }

        return rec;
    }

    public NtfsAttribute? FindFirst(uint type, string name = "")
    {
        foreach (var a in Attributes)
            if (a.Type == type && a.Name.Equals(name, StringComparison.Ordinal))
                return a;
        return null;
    }

    public IEnumerable<NtfsAttribute> All(uint type)
        => Attributes.Where(a => a.Type == type);

    public NtfsAttribute? DataAttribute(string name = "")
        => FindFirst(AttrType.Data, name);
}
