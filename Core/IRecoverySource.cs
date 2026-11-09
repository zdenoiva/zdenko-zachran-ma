namespace NtfsRescue.Core;

public sealed class RecoveryEntry
{
    public string Name = "";
    public string FullPath = "";
    public string Status = "";
    public long Size;
    public DateTime Modified;
    public bool IsDirectory;
    public bool Deleted;
    public object? Native;

    public override string ToString() => FullPath;
}

public interface IRecoverySource : IDisposable
{
    string FileSystemName { get; }
    string SourceName { get; }
    int DiskNumber { get; }
    long TotalBytes { get; }
    List<RecoveryEntry> Entries { get; }

    void Load(bool includeDeleted, IProgress<ScanProgress>? progress, CancellationToken ct);
    void CopyFile(RecoveryEntry entry, Stream output, Action<long>? progress,
        CancellationToken ct, Action<string>? warn);
}
