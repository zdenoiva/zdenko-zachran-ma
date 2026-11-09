using System.Text;

namespace NtfsRescue.Core;

public sealed class NtfsEntry
{
    public ulong RecordNumber;
    public ulong ParentRecord;
    public ushort Sequence;
    public string Name = "";
    public string ParentPath = "";
    public string FullPath = "";
    public bool IsDirectory;
    public bool InUse;
    public bool Deleted;
    public long Size;
    public long AllocatedSize;
    public DateTime Created;
    public DateTime Modified;
    public uint FileAttributes;
    public bool Compressed;
    public bool Encrypted;
    public bool Sparse;
    public bool ReparsePoint;
    public byte NameNamespace;
    public string Status = "";

    public bool IsSystem => RecordNumber < 16;

    public override string ToString() => FullPath;
}

public sealed record ScanProgress(int Current, int Total, string Phase);

public sealed class NtfsScanResult
{
    public List<NtfsEntry> Entries = new();
    public int RecordsRead;
    public int CorruptRecords;
    public int InUseRecords;
    public long TotalBytes;
}

public static class NtfsScanner
{
    public static NtfsScanResult Scan(
        NtfsVolume volume,
        bool includeDeleted,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        var records = new List<NtfsRecord>();
        int corrupt = 0;
        int inUse = 0;
        long total = volume.TotalRecords;

        for (ulong i = 1; i < (ulong)total; i++)
        {
            if ((i & 0x3FF) == 0)
                progress?.Report(new ScanProgress((int)i, (int)Math.Min(total, int.MaxValue), "Čítam MFT"));

            if ((i & 0xFFF) == 0) ct.ThrowIfCancellationRequested();

            NtfsRecord? rec;
            try { rec = volume.ReadMftRecord(i); }
            catch (OperationCanceledException) { throw; }
            catch { corrupt++; continue; }

            if (rec is null) { corrupt++; continue; }
            if (rec.InUse) inUse++;
            if (rec.BaseRecord != 0) continue;
            records.Add(rec);
        }

        var result = NtfsEntryBuilder.Build(records, includeDeleted, progress, ct);
        result.RecordsRead = records.Count;
        result.CorruptRecords = corrupt;
        result.InUseRecords = inUse;
        return result;
    }
}

public static class NtfsEntryBuilder
{
    private const ulong RootRecord = 5;

    public static NtfsScanResult Build(
        IReadOnlyList<NtfsRecord> records,
        bool includeDeleted,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        var result = new NtfsScanResult();
        var entries = result.Entries;
        int index = 0;

        foreach (var rec in records)
        {
            ct.ThrowIfCancellationRequested();
            if ((++index & 0x3FF) == 0)
                progress?.Report(new ScanProgress(index, records.Count, "Skladám súbory"));

            if (!rec.InUse && !includeDeleted) continue;
            if (rec.Number < 16) continue;
            if (rec.BaseRecord != 0) continue;

            var si = rec.FindFirst(AttrType.StandardInformation);
            var fnAttrs = rec.All(AttrType.FileName).ToList();
            if (fnAttrs.Count == 0) continue;

            var parsedFns = new List<FileInfo>();
            foreach (var fa in fnAttrs)
            {
                var info = ParseFileName(fa);
                if (info is not null) parsedFns.Add(info);
            }
            if (parsedFns.Count == 0) continue;

            var hasLong = parsedFns.Any(f => f.Namespace != 2);
            var chosen = hasLong ? parsedFns.Where(f => f.Namespace != 2).ToList() : parsedFns;

            var seen = new HashSet<(ulong, string)>();
            uint attrs = 0;
            DateTime created = DateTime.MinValue, modified = DateTime.MinValue;
            if (si?.ResidentValue is { Length: >= 36 } sv)
            {
                created = FileTime(Bin.I64(sv, 0));
                modified = FileTime(Bin.I64(sv, 8));
                attrs = Bin.U32(sv, 32);
            }

            foreach (var fn in chosen)
            {
                if (fn.Parent == 11) continue; // $Extend
                if (!seen.Add((fn.Parent, fn.Name))) continue;

                var e = new NtfsEntry
                {
                    RecordNumber = rec.Number,
                    ParentRecord = fn.Parent,
                    Sequence = rec.Sequence,
                    Name = fn.Name,
                    IsDirectory = rec.IsDirectory,
                    InUse = rec.InUse,
                    Deleted = !rec.InUse,
                    Size = rec.IsDirectory ? 0 : Math.Max(fn.RealSize, 0),
                    AllocatedSize = Math.Max(fn.AllocatedSize, 0),
                    Created = created == DateTime.MinValue ? fn.Created : created,
                    Modified = modified == DateTime.MinValue ? fn.Modified : modified,
                    FileAttributes = attrs,
                    Encrypted = (attrs & 0x4000) != 0,
                    Compressed = (attrs & 0x0800) != 0,
                    Sparse = (attrs & 0x0200) != 0,
                    ReparsePoint = (attrs & 0x0400) != 0,
                    NameNamespace = fn.Namespace
                };

                e.Status = !rec.InUse ? "Zmazaný"
                    : e.Encrypted ? "Šifrovaný (EFS)"
                    : e.Compressed ? "Komprimovaný"
                    : e.Sparse ? "Sparse"
                    : e.ReparsePoint ? "Reparse point"
                    : "OK";

                entries.Add(e);
            }
        }

        BuildPaths(entries);
        result.TotalBytes = entries.Where(e => !e.IsDirectory).Sum(e => e.Size);
        return result;
    }

    private static void BuildPaths(List<NtfsEntry> entries)
    {
        var byRecord = new Dictionary<ulong, List<NtfsEntry>>();
        foreach (var e in entries)
        {
            var list = byRecord.TryGetValue(e.RecordNumber, out var l)
                ? l
                : (byRecord[e.RecordNumber] = new List<NtfsEntry>());
            list.Add(e);
        }

        var dirs = new Dictionary<ulong, NtfsEntry>();
        foreach (var e in entries)
        {
            if (!e.IsDirectory) continue;
            if (!dirs.ContainsKey(e.RecordNumber)) dirs[e.RecordNumber] = e;
        }

        var cache = new Dictionary<ulong, string>();

        string DirectoryPath(ulong record, HashSet<ulong> visiting, int depth)
        {
            if (record == RootRecord) return "";
            if (cache.TryGetValue(record, out var cached)) return cached;
            if (depth > 64 || !visiting.Add(record)) return "(Osirotené)";

            var dir = dirs.GetValueOrDefault(record);
            if (dir is null)
            {
                visiting.Remove(record);
                return "(Osirotené)";
            }

            var parentPath = DirectoryPath(dir.ParentRecord, visiting, depth + 1);
            visiting.Remove(record);
            var path = parentPath.Length == 0 ? dir.Name : parentPath + "\\" + dir.Name;
            cache[record] = path;
            return path;
        }

        foreach (var e in entries)
        {
            if (e.RecordNumber == RootRecord)
            {
                e.ParentPath = "";
                e.FullPath = "";
                continue;
            }

            var parentPath = DirectoryPath(e.ParentRecord, new HashSet<ulong>(), 0);
            e.ParentPath = parentPath;
            e.FullPath = parentPath.Length == 0 ? e.Name : parentPath + "\\" + e.Name;
            if (parentPath == "(Osirotené)" && e.ParentRecord != RootRecord)
                e.FullPath = "(Osirotené)\\" + e.Name;
        }
    }

    private sealed record FileInfo(
        ulong Parent,
        string Name,
        byte Namespace,
        long AllocatedSize,
        long RealSize,
        DateTime Created,
        DateTime Modified);

    private static FileInfo? ParseFileName(NtfsAttribute attr)
    {
        var v = attr.ResidentValue;
        if (v is null || v.Length < 66) return null;

        ulong parent = Bin.U64(v, 0) & 0xFFFFFFFFFFFF;
        long allocated = Bin.I64(v, 0x28);
        long real = Bin.I64(v, 0x30);
        int nameLength = v[0x40];
        byte ns = v[0x41];

        if (0x42 + nameLength * 2 > v.Length) return null;

        var name = Encoding.Unicode.GetString(v, 0x42, nameLength * 2);
        if (string.IsNullOrEmpty(name)) return null;

        return new FileInfo(parent, name, ns, allocated, real,
            FileTime(Bin.I64(v, 0x08)), FileTime(Bin.I64(v, 0x10)));
    }

    public static DateTime FileTime(long value)
    {
        if (value <= 0) return DateTime.MinValue;
        try { return DateTime.FromFileTimeUtc(value).ToLocalTime(); }
        catch { return DateTime.MinValue; }
    }
}
