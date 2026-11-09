namespace NtfsRescue.Core.Fat32;

public static class Fat32Scanner
{
    public static List<Fat32Entry> Scan(
        Fat32Volume volume,
        bool includeDeleted,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        var result = new List<Fat32Entry>();
        if (volume.Boot.RootClusterInt < 2) return result;

        var queue = new Queue<(uint Cluster, string Path, int Depth)>();
        var visited = new HashSet<uint>();
        queue.Enqueue(((uint)volume.Boot.RootClusterInt, "", 0));
        visited.Add((uint)volume.Boot.RootClusterInt);

        int dirs = 0;
        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (cluster, path, depth) = queue.Dequeue();
            dirs++;

            var lfn = new List<Fat32DirEntry>();
            foreach (var de in volume.EnumerateDirectory(cluster, ct))
            {
                if (de.IsLfn)
                {
                    lfn.Add(de);
                    continue;
                }

                var checksum = Fat32DirEntry.Checksum(de.Raw);
                var longName = lfn.Any(p => p.Deleted)
                    ? ""
                    : Fat32DirEntry.BuildLongName(lfn, checksum);
                lfn.Clear();

                if (de.IsVolumeLabel) continue;

                var name = !string.IsNullOrWhiteSpace(longName) ? longName : de.ShortName;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (name is "." or "..") continue;

                bool deleted = de.Deleted;
                if (deleted && de.IsDirectory) continue;

                var full = path.Length == 0 ? name : path + "\\" + name;
                var entry = new Fat32Entry
                {
                    Name = name,
                    FullPath = full,
                    IsDirectory = de.IsDirectory,
                    Deleted = deleted,
                    Size = de.IsDirectory ? 0 : Math.Max(0, de.Size),
                    FirstCluster = de.FirstCluster,
                    Attributes = de.Attributes,
                    Modified = de.Modified,
                    Created = de.Created
                };

                if (deleted)
                {
                    entry.Status = !volume.UseFat ? "Zmazaný (FAT poškodená)"
                        : de.FirstCluster < 2 ? "Zmazaný (bez klastra)"
                        : volume.GetFatEntry(de.FirstCluster) == 0 ? "Zmazaný (reťazec chýba)"
                        : "Zmazaný";
                }
                else
                {
                    entry.Status = volume.UseFat ? "OK" : "FAT poškodená (súvislé čítanie)";
                }

                if (deleted && !includeDeleted) { }
                else result.Add(entry);

                if (de.IsDirectory && !deleted && de.FirstCluster >= 2 && depth < 64)
                {
                    if (visited.Add(de.FirstCluster))
                        queue.Enqueue((de.FirstCluster, full, depth + 1));
                }
            }

            if ((dirs & 0x1F) == 0)
                progress?.Report(new ScanProgress(result.Count, 0, $"FAT32: {dirs} adresárov"));
        }

        result.Sort((a, b) => string.Compare(a.FullPath, b.FullPath, StringComparison.OrdinalIgnoreCase));
        progress?.Report(new ScanProgress(result.Count, 0, "FAT32 hotovo"));
        return result;
    }
}

public sealed class Fat32RecoverySource : IRecoverySource
{
    private readonly RawDisk _disk;
    private readonly long _partitionOffset;
    private readonly Fat32BootSector _boot;
    private Fat32Volume? _volume;

    public string FileSystemName => "FAT32";
    public string SourceName => $"Disk {_disk.DiskNumber} · FAT32";
    public int DiskNumber => _disk.DiskNumber;
    public long TotalBytes { get; private set; }
    public List<RecoveryEntry> Entries { get; } = new();

    public Fat32RecoverySource(RawDisk disk, long partitionOffset, Fat32BootSector boot)
    {
        _disk = disk;
        _partitionOffset = partitionOffset;
        _boot = boot;
    }

    public void Load(bool includeDeleted, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        _volume = new Fat32Volume(_disk, _partitionOffset, _boot);
        _volume.LoadFat(ct);
        var entries = Fat32Scanner.Scan(_volume, includeDeleted, progress, ct);

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
        if (_volume is null) throw new InvalidOperationException("FAT32 zdroj nie je načítaný.");
        if (entry.Native is not Fat32Entry fat) throw new InvalidOperationException("Neplatný FAT32 záznam.");
        _volume.ReadFile(fat, output, progress, ct, warn);
    }

    public void Dispose()
    {
        try { _disk.Dispose(); } catch { }
    }
}
