namespace NtfsRescue.Core;

public static class NtfsIo
{
    public static void ReadStreamBytes(
        RawDisk disk,
        long partitionOffset,
        int clusterSize,
        IReadOnlyList<NtfsRun> runs,
        long offset,
        byte[] buffer,
        int bufferOffset,
        int length)
    {
        if (length <= 0) return;

        long position = offset;
        int dest = bufferOffset;
        int remaining = length;
        long cs = clusterSize;

        while (remaining > 0)
        {
            long vcn = position / cs;
            long offsetInCluster = position % cs;

            int runIndex = -1;
            for (int i = 0; i < runs.Count; i++)
            {
                var r = runs[i];
                if (vcn >= r.Vcn && vcn < r.EndVcn) { runIndex = i; break; }
            }

            if (runIndex < 0)
            {
                long nextVcn = long.MaxValue;
                foreach (var r in runs)
                    if (r.Vcn > vcn) { nextVcn = r.Vcn; break; }

                long gapClusters = nextVcn == long.MaxValue
                    ? (remaining / cs) + 1
                    : nextVcn - vcn;
                long gapBytes = gapClusters * cs - offsetInCluster;
                int chunk = (int)Math.Min(remaining, Math.Max(1, gapBytes));
                Array.Clear(buffer, dest, chunk);
                position += chunk; dest += chunk; remaining -= chunk;
                continue;
            }

            var run = runs[runIndex];
            long bytesInRun = (run.EndVcn - vcn) * cs - offsetInCluster;
            int readChunk = (int)Math.Min(remaining, Math.Max(1, bytesInRun));

            if (run.IsSparse)
            {
                Array.Clear(buffer, dest, readChunk);
            }
            else
            {
                long lcn = run.Lcn + (vcn - run.Vcn);
                long physical = partitionOffset + lcn * cs + offsetInCluster;
                disk.ReadAt(physical, buffer, dest, readChunk);
            }

            position += readChunk;
            dest += readChunk;
            remaining -= readChunk;
        }
    }

    public static void ReadClusters(RawDisk disk, long partitionOffset, int clusterSize,
        long lcn, long count, byte[] buffer, int bufferOffset)
    {
        long length = count * clusterSize;
        if (length <= 0) return;
        disk.ReadAt(partitionOffset + lcn * clusterSize, buffer, bufferOffset, (int)length);
    }
}
