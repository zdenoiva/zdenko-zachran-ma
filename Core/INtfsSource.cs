namespace NtfsRescue.Core;

public interface INtfsSource
{
    RawDisk Disk { get; }
    long PartitionOffset { get; }
    int ClusterSize { get; }
    int RecordSize { get; }
    NtfsRecord? ReadRecord(ulong number);
    void ReadStreamBytes(IReadOnlyList<NtfsRun> runs, long offset, byte[] buffer, int bufferOffset, int length);
    void ReadClusters(long lcn, long count, byte[] buffer, int bufferOffset);
}
