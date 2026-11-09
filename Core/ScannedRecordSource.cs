namespace NtfsRescue.Core;

public sealed class ScannedRecordSource : INtfsSource
{
    private readonly RawDisk _disk;
    private readonly long _partitionOffset;
    private readonly int _sectorSize;
    private readonly Dictionary<ulong, ScannedRecord> _index = new();
    private readonly Dictionary<ulong, NtfsRecord> _cache = new();

    public RawDisk Disk => _disk;
    public long PartitionOffset => _partitionOffset;
    public int ClusterSize { get; }
    public int RecordSize { get; }
    public List<NtfsEntry> Entries { get; }

    public ScannedRecordSource(
        RawDisk disk,
        long partitionOffset,
        int sectorSize,
        int clusterSize,
        int recordSize,
        IReadOnlyList<ScannedRecord> records,
        List<NtfsEntry> entries)
    {
        _disk = disk;
        _partitionOffset = partitionOffset;
        _sectorSize = sectorSize;
        ClusterSize = clusterSize;
        RecordSize = recordSize;
        Entries = entries;

        foreach (var group in records.GroupBy(r => r.Number))
        {
            var best = group
                .OrderByDescending(r => r.BaseRecord == 0)
                .ThenByDescending(r => r.InUse)
                .ThenByDescending(r => r.Sequence)
                .First();
            _index[best.Number] = best;
        }
    }

    public NtfsRecord? ReadRecord(ulong number)
    {
        if (_cache.TryGetValue(number, out var cached)) return cached;
        if (!_index.TryGetValue(number, out var sr)) return null;

        try
        {
            var raw = new byte[sr.RecordSize];
            _disk.ReadAt(_partitionOffset + sr.DiskOffset, raw, raw.Length);
            var rec = NtfsRecord.TryParse(raw, number, _sectorSize, out _);
            if (rec is null) return null;
            rec.Number = number;
            if (_cache.Count > 20000) _cache.Clear();
            _cache[number] = rec;
            return rec;
        }
        catch
        {
            return null;
        }
    }

    public void ReadStreamBytes(IReadOnlyList<NtfsRun> runs, long offset, byte[] buffer,
        int bufferOffset, int length)
        => NtfsIo.ReadStreamBytes(_disk, _partitionOffset, ClusterSize, runs, offset, buffer, bufferOffset, length);

    public void ReadClusters(long lcn, long count, byte[] buffer, int bufferOffset)
        => NtfsIo.ReadClusters(_disk, _partitionOffset, ClusterSize, lcn, count, buffer, bufferOffset);
}
