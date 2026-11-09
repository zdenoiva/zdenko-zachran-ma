namespace NtfsRescue.Core;

public sealed class NtfsRecoverySource : IRecoverySource
{
    private readonly RawDisk _disk;
    private readonly INtfsSource _source;
    private readonly bool _ownsDisk;

    public string FileSystemName => "NTFS";
    public string SourceName => $"Disk {_disk.DiskNumber} · NTFS";
    public int DiskNumber => _disk.DiskNumber;
    public long TotalBytes { get; private set; }
    public List<RecoveryEntry> Entries { get; } = new();

    public NtfsRecoverySource(RawDisk disk, long partitionOffset, long partitionLength)
    {
        _disk = disk;
        _ownsDisk = true;
        _source = NtfsVolume.Open(disk, partitionOffset, partitionLength);
    }

    public NtfsRecoverySource(RawDisk disk, INtfsSource source, bool ownsDisk)
    {
        _disk = disk;
        _source = source;
        _ownsDisk = ownsDisk;
    }

    public void Load(bool includeDeleted, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        if (_source is NtfsVolume volume)
        {
            var scan = NtfsScanner.Scan(volume, includeDeleted, progress, ct);
            FillEntries(scan.Entries);
            return;
        }

        // Hĺbkový sken: záznamy už dodal ScannedRecordSource.
        if (_source is ScannedRecordSource deep)
        {
            FillEntries(deep.Entries);
            return;
        }

        throw new InvalidOperationException("Neznámy NTFS zdroj.");
    }

    private void FillEntries(List<NtfsEntry> entries)
    {
        Entries.Clear();
        foreach (var e in entries)
        {
            Entries.Add(new RecoveryEntry
            {
                Name = e.Name,
                FullPath = e.FullPath,
                Status = e.Status,
                Size = e.Size,
                Modified = e.Modified,
                IsDirectory = e.IsDirectory,
                Deleted = e.Deleted,
                Native = e
            });
        }
        TotalBytes = Entries.Where(e => !e.IsDirectory).Sum(e => Math.Max(0, e.Size));
    }

    public void CopyFile(RecoveryEntry entry, Stream output, Action<long>? progress,
        CancellationToken ct, Action<string>? warn)
    {
        if (entry.Native is not NtfsEntry ntfs)
            throw new InvalidOperationException("Neplatný NTFS záznam.");

        var reader = new NtfsDataReader(_source, ntfs);
        reader.CopyTo(output, progress, ct, warn);
    }

    public void Dispose()
    {
        if (_ownsDisk)
        {
            try { _disk.Dispose(); } catch { }
        }
    }
}
