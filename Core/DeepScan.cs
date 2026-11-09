using System.Text.Json;
using System.Text.Json.Serialization;
using NtfsRescue.Util;

namespace NtfsRescue.Core;

public sealed class ScannedRecord
{
    public ulong Number { get; set; }
    public ushort Sequence { get; set; }
    public ulong BaseRecord { get; set; }
    public ushort Flags { get; set; }
    public long DiskOffset { get; set; }
    public int RecordSize { get; set; }

    [JsonIgnore] public bool InUse => (Flags & 0x01) != 0;
    [JsonIgnore] public bool IsDirectory => (Flags & 0x02) != 0;
}

public sealed class MftFragment
{
    public long LogicalStart { get; set; }
    public long ClusterStart { get; set; }
    public long ClusterCount { get; set; }
    public int RecordSize { get; set; }
}

public sealed class DeepScanOptions
{
    public bool IncludeDeleted;
    public bool Resume = true;
    public string? IndexPath;
}

public sealed record DeepScanProgress(long BytesDone, long BytesTotal, int Records, string Phase);

public sealed class DeepScanResult
{
    public NtfsBootSector? Boot;
    public byte[]? BootRaw;
    public bool BootFromBackup;
    public int SectorSize = 512;
    public int ClusterSize = 4096;
    public int RecordSize = 1024;
    public long PartitionOffset;
    public long PartitionLength;
    public long MftStartOffset;
    public List<ScannedRecord> Records = new();
    public List<NtfsEntry> Entries = new();
    public List<MftFragment> Fragments = new();
    public int CorruptCandidates;
    public string IndexPath = "";
}

public sealed class DeepScanIndex
{
    public int Version { get; set; } = 1;
    public int DiskNumber { get; set; }
    public long PartitionOffset { get; set; }
    public long PartitionLength { get; set; }
    public int SectorSize { get; set; }
    public int ClusterSize { get; set; }
    public long LastOffset { get; set; }
    public List<ScannedRecord> Records { get; set; } = new();
}

public static class DeepMftScanner
{
    private const int ChunkSize = 8 * 1024 * 1024;
    private static readonly byte[] FileSignature = "FILE"u8.ToArray();

    public static DeepScanResult Scan(
        RawDisk disk,
        long partitionOffset,
        long partitionLength,
        DeepScanOptions options,
        IProgress<DeepScanProgress>? progress,
        CancellationToken ct)
    {
        var result = new DeepScanResult
        {
            PartitionOffset = partitionOffset,
            PartitionLength = partitionLength
        };

        if (partitionLength <= 0)
            partitionLength = Math.Max(0, disk.Size - partitionOffset);

        var bootRaw = disk.ReadBytes(partitionOffset, 512);
        var boot = NtfsBootSector.Parse(bootRaw);
        result.BootRaw = bootRaw;
        result.Boot = boot;

        if (!boot.IsValid && partitionLength > 512)
        {
            try
            {
                var backupRaw = disk.ReadBytes(partitionOffset + partitionLength - disk.SectorSize, 512);
                var backup = NtfsBootSector.Parse(backupRaw);
                if (backup.IsValid)
                {
                    result.Boot = backup;
                    result.BootRaw = backupRaw;
                    result.BootFromBackup = true;
                    boot = backup;
                }
            }
            catch { }
        }

        result.SectorSize = boot.IsValid ? boot.BytesPerSector : 512;
        result.ClusterSize = boot.IsValid ? boot.ClusterSize : 4096;
        result.RecordSize = boot.IsValid ? boot.MftRecordSize : 1024;
        result.MftStartOffset = boot.IsValid ? boot.MftLcn * boot.ClusterSize : 0;

        var indexPath = options.IndexPath ?? Path.Combine(AppPaths.Base,
            $"deep-{disk.DiskNumber}-{partitionOffset}.json");
        result.IndexPath = indexPath;
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath) ?? ".");

        var trySizes = boot.IsValid
            ? new[] { boot.MftRecordSize }
            : new[] { 1024, 4096 };

        var fragments = TryBuildFragmentsFromMft(disk, partitionOffset, partitionLength, boot);
        var index = options.Resume ? TryLoadIndex(indexPath, disk.DiskNumber, partitionOffset, partitionLength) : null;
        var records = index?.Records ?? new List<ScannedRecord>();
        long offset = index?.LastOffset ?? 0;
        long lastSave = offset;

        progress?.Report(new DeepScanProgress(offset, partitionLength, records.Count,
            offset > 0 ? "Pokračujem v hĺbkovom skene" : "Hĺbkový sken"));

        while (offset < partitionLength)
        {
            ct.ThrowIfCancellationRequested();
            int chunk = (int)Math.Min(ChunkSize, partitionLength - offset);
            chunk = (chunk / result.SectorSize) * result.SectorSize;
            if (chunk <= 0) break;

            var buffer = new byte[chunk];
            disk.ReadAt(partitionOffset + offset, buffer, chunk);
            ScanChunk(buffer, chunk, offset, trySizes, result.SectorSize, result.ClusterSize,
                fragments, records, ct);

            offset += chunk;
            progress?.Report(new DeepScanProgress(offset, partitionLength, records.Count,
                "Hľadám FILE záznamy"));

            if (offset - lastSave >= 4L * 1024 * 1024 * 1024 || offset >= partitionLength)
            {
                SaveIndex(indexPath, disk.DiskNumber, partitionOffset, partitionLength,
                    result.SectorSize, result.ClusterSize, offset, records);
                lastSave = offset;
            }
        }

        var deduped = Deduplicate(records);
        result.Records = deduped;

        var baseRecords = new List<NtfsRecord>();
        foreach (var sr in deduped.Where(r => r.BaseRecord == 0))
        {
            var rec = ReadScannedRecord(disk, partitionOffset, sr, result.SectorSize);
            if (rec is not null) baseRecords.Add(rec);
        }

        var scan = NtfsEntryBuilder.Build(baseRecords, options.IncludeDeleted, null, ct);
        result.Entries = scan.Entries;
        result.Fragments = ReconstructFragments(deduped, result.ClusterSize, result.SectorSize);

        progress?.Report(new DeepScanProgress(partitionLength, partitionLength,
            deduped.Count, "Hĺbkový sken dokončený"));
        return result;
    }

    private static void ScanChunk(
        byte[] buffer,
        int length,
        long baseOffset,
        int[] recordSizes,
        int sectorSize,
        int clusterSize,
        List<MftFragment> fragments,
        List<ScannedRecord> records,
        CancellationToken ct)
    {
        var span = buffer.AsSpan(0, length);
        int search = 0;

        while (search <= span.Length - 4)
        {
            ct.ThrowIfCancellationRequested();
            int idx = span[search..].IndexOf(FileSignature);
            if (idx < 0) break;
            int i = search + idx;
            long global = baseOffset + i;
            bool found = false;

            foreach (int recordSize in recordSizes)
            {
                if (recordSize <= 0 || global % recordSize != 0) continue;
                if (i + recordSize > span.Length) continue;

                var raw = span.Slice(i, recordSize).ToArray();
                var rec = NtfsRecord.TryParse(raw, 0, sectorSize, out _);
                if (rec is null) continue;

                ulong number = rec.MftRecordNumber;
                if (number == 0)
                    number = ResolveNumber(global, fragments, clusterSize, recordSize) ??
                             (ulong)(global / recordSize);
                rec.Number = number;

                records.Add(new ScannedRecord
                {
                    Number = number,
                    Sequence = rec.Sequence,
                    BaseRecord = rec.BaseRecord,
                    Flags = (ushort)((rec.InUse ? 0x01 : 0) | (rec.IsDirectory ? 0x02 : 0)),
                    DiskOffset = global,
                    RecordSize = recordSize
                });

                search = i + recordSize;
                found = true;
                break;
            }

            if (!found) search = i + 4;
        }
    }

    private static ulong? ResolveNumber(long diskOffset, List<MftFragment> fragments,
        int clusterSize, int recordSize)
    {
        if (fragments.Count == 0) return null;
        long cluster = diskOffset / clusterSize;
        long inCluster = diskOffset % clusterSize;

        foreach (var frag in fragments)
        {
            if (frag.RecordSize != 0 && frag.RecordSize != recordSize) continue;
            if (cluster < frag.ClusterStart || cluster >= frag.ClusterStart + frag.ClusterCount) continue;

            long bytesIntoFragment = (cluster - frag.ClusterStart) * clusterSize + inCluster;
            return (ulong)(frag.LogicalStart + bytesIntoFragment / recordSize);
        }
        return null;
    }

    private static List<MftFragment> TryBuildFragmentsFromMft(
        RawDisk disk, long partitionOffset, long partitionLength, NtfsBootSector boot)
    {
        var fragments = new List<MftFragment>();
        if (!boot.IsValid) return fragments;

        try
        {
            var volume = NtfsVolume.Open(disk, partitionOffset, partitionLength);
            int rpc = Math.Max(1, volume.ClusterSize / volume.RecordSize);
            long logical = 0;
            foreach (var run in volume.MftRuns)
            {
                if (run.IsSparse)
                {
                    logical += run.Clusters * rpc;
                    continue;
                }
                fragments.Add(new MftFragment
                {
                    LogicalStart = logical,
                    ClusterStart = run.Lcn,
                    ClusterCount = run.Clusters,
                    RecordSize = volume.RecordSize
                });
                logical += run.Clusters * rpc;
            }
        }
        catch { }
        return fragments;
    }

    private static List<ScannedRecord> Deduplicate(List<ScannedRecord> records)
    {
        return records
            .GroupBy(r => r.Number)
            .Select(g => g
                .OrderByDescending(r => r.BaseRecord == 0)
                .ThenByDescending(r => r.InUse)
                .ThenByDescending(r => r.Sequence)
                .ThenByDescending(r => r.RecordSize)
                .First())
            .OrderBy(r => r.Number)
            .ToList();
    }

    private static NtfsRecord? ReadScannedRecord(RawDisk disk, long partitionOffset,
        ScannedRecord sr, int sectorSize)
    {
        try
        {
            var raw = new byte[sr.RecordSize];
            disk.ReadAt(partitionOffset + sr.DiskOffset, raw, raw.Length);
            var rec = NtfsRecord.TryParse(raw, sr.Number, sectorSize, out _);
            if (rec is null) return null;
            rec.Number = sr.Number;
            return rec;
        }
        catch
        {
            return null;
        }
    }

    private static List<MftFragment> ReconstructFragments(
        List<ScannedRecord> records, int clusterSize, int sectorSize)
    {
        var groups = new Dictionary<(int RecordSize, long Delta), List<ScannedRecord>>();

        foreach (var r in records.Where(r => r.BaseRecord == 0 && r.Number > 0))
        {
            long delta = r.DiskOffset - (long)r.Number * r.RecordSize;
            var key = (r.RecordSize, delta);
            if (!groups.TryGetValue(key, out var list))
                groups[key] = list = new List<ScannedRecord>();
            list.Add(r);
        }

        var fragments = new List<MftFragment>();
        foreach (var ((recordSize, delta), list) in groups)
        {
            if (list.Count == 0) continue;
            long min = (long)list.Min(r => r.Number);
            long max = (long)list.Max(r => r.Number);
            int rpc = Math.Max(1, clusterSize / recordSize);
            long logicalStart = min - (min % rpc);
            long logicalEnd = (max / rpc + 1) * rpc;
            long clusterCount = (logicalEnd - logicalStart) / rpc;
            long physical = delta + logicalStart * recordSize;

            if (clusterCount < 2 || physical < 0 || physical % clusterSize != 0) continue;
            fragments.Add(new MftFragment
            {
                LogicalStart = logicalStart,
                ClusterStart = physical / clusterSize,
                ClusterCount = clusterCount,
                RecordSize = recordSize
            });
        }

        fragments.Sort((a, b) => a.LogicalStart.CompareTo(b.LogicalStart));
        var merged = new List<MftFragment>();
        foreach (var f in fragments)
        {
            if (merged.Count == 0) { merged.Add(f); continue; }
            var last = merged[^1];
            if (last.RecordSize == f.RecordSize &&
                last.LogicalStart + last.ClusterCount * Math.Max(1, clusterSize / f.RecordSize) == f.LogicalStart &&
                last.ClusterStart + last.ClusterCount == f.ClusterStart)
            {
                last.ClusterCount += f.ClusterCount;
            }
            else
            {
                merged.Add(f);
            }
        }
        return merged;
    }

    private static DeepScanIndex? TryLoadIndex(string path, int diskNumber,
        long partitionOffset, long partitionLength)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var index = JsonSerializer.Deserialize<DeepScanIndex>(File.ReadAllText(path));
            if (index is null || index.Version != 1) return null;
            if (index.DiskNumber != diskNumber) return null;
            if (index.PartitionOffset != partitionOffset) return null;
            if (index.PartitionLength != partitionLength) return null;
            if (index.LastOffset < 0 || index.LastOffset > partitionLength) index.LastOffset = 0;
            return index;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveIndex(string path, int diskNumber, long partitionOffset,
        long partitionLength, int sectorSize, int clusterSize, long lastOffset,
        List<ScannedRecord> records)
    {
        try
        {
            var index = new DeepScanIndex
            {
                DiskNumber = diskNumber,
                PartitionOffset = partitionOffset,
                PartitionLength = partitionLength,
                SectorSize = sectorSize,
                ClusterSize = clusterSize,
                LastOffset = lastOffset,
                Records = records
            };
            File.WriteAllText(path, JsonSerializer.Serialize(index));
        }
        catch { }
    }
}
