using System.Diagnostics;
using System.Globalization;
using System.Text;
using NtfsRescue.Core;
using NtfsRescue.Util;

namespace NtfsRescue.Repair;

public sealed class SurfaceBlock
{
    public long Offset;
    public int Length;
    public double Ms;
    public bool Bad;
}

public sealed record SurfaceProgress(
    long DoneBytes,
    long TotalBytes,
    int BlocksDone,
    int TotalBlocks,
    double BytesPerSecond,
    TimeSpan Elapsed,
    TimeSpan Eta,
    int LastBlockIndex);

public sealed class SurfaceScanResult
{
    public readonly List<SurfaceBlock> Blocks = new();
    public readonly List<long> BadSectors = new();
    public int BadBlocks;
    public int SlowBlocks;
    public long TotalBytes;
    public TimeSpan Duration;
    public string Summary = "";

    public void SaveReport(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("offset,length,ms,status");
        foreach (var block in Blocks)
        {
            var status = block.Bad ? "CHYBA" : block.Ms >= SurfaceScanner.VerySlowMs ? "VELMI_POMALE"
                : block.Ms >= SurfaceScanner.SlowMs ? "POMALE" : "OK";
            sb.AppendLine($"{block.Offset},{block.Length}," +
                          $"{block.Ms.ToString("F1", CultureInfo.InvariantCulture)},{status}");
        }
        sb.AppendLine();
        sb.AppendLine("# vadne sektory");
        foreach (var offset in BadSectors) sb.AppendLine(offset.ToString());
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }
}

public static class SurfaceScanner
{
    public const int BlockSize = 4 * 1024 * 1024;
    public const double SlowMs = 100;
    public const double VerySlowMs = 250;

    public static SurfaceScanResult Scan(DiskInfo disk, IProgress<SurfaceProgress>? progress,
        Action<string>? log, CancellationToken ct, Action<int, SurfaceBlock>? onBlock = null)
    {
        if (disk.Size <= 0) throw new InvalidOperationException("Neznáma veľkosť disku.");

        var totalBlocks = (int)Math.Min(int.MaxValue, (disk.Size + BlockSize - 1) / BlockSize);
        var result = new SurfaceScanResult { TotalBytes = disk.Size };
        var badSectors = new HashSet<long>();
        var buffer = new byte[BlockSize];
        var sectorBuffer = new byte[512];
        var stopwatch = Stopwatch.StartNew();
        var lastReport = 0L;

        using var reader = new DirectDiskReader(disk.Number);
        if (!reader.Direct)
            log?.Invoke("Priame čítanie nie je dostupné; používam bežné čítanie (môže byť ovplyvnené cache).");
        for (int i = 0; i < totalBlocks; i++)
        {
            ct.ThrowIfCancellationRequested();
            long offset = (long)i * BlockSize;
            int length = (int)Math.Min(BlockSize, disk.Size - offset);
            var blockWatch = Stopwatch.StartNew();
            var block = new SurfaceBlock { Offset = offset, Length = length };
            try
            {
                reader.ReadAt(offset, buffer, length);
                block.Ms = blockWatch.Elapsed.TotalMilliseconds;
            }
            catch
            {
                block.Bad = true;
                block.Ms = blockWatch.Elapsed.TotalMilliseconds;
                result.BadBlocks++;
                CheckBadSectors(reader, offset, length, sectorBuffer, badSectors, ct);
            }

            if (!block.Bad && block.Ms >= SlowMs) result.SlowBlocks++;
            result.Blocks.Add(block);
            onBlock?.Invoke(i, block);

            if (stopwatch.ElapsedMilliseconds - lastReport >= 500 || i == totalBlocks - 1)
            {
                lastReport = stopwatch.ElapsedMilliseconds;
                var done = Math.Min(disk.Size, offset + length);
                var speed = stopwatch.Elapsed.TotalSeconds > 0 ? done / stopwatch.Elapsed.TotalSeconds : 0;
                var eta = speed > 0 ? TimeSpan.FromSeconds((disk.Size - done) / speed) : TimeSpan.Zero;
                progress?.Report(new SurfaceProgress(done, disk.Size, i + 1, totalBlocks, speed,
                    stopwatch.Elapsed, eta, i));
            }
        }

        result.BadSectors.AddRange(badSectors.OrderBy(x => x));
        result.Duration = stopwatch.Elapsed;
        result.Summary =
            $"Povrch: {Fmt.Bytes(disk.Size)} · {result.Blocks.Count:N0} blokov · " +
            $"{result.BadBlocks:N0} chybných blokov · {result.BadSectors.Count:N0} vadných sektorov · " +
            $"{result.SlowBlocks:N0} pomalých blokov · {result.Duration:hh\\:mm\\:ss}";
        log?.Invoke(result.Summary);
        return result;
    }

    private static void CheckBadSectors(DirectDiskReader reader, long offset, int length, byte[] buffer,
        HashSet<long> badSectors, CancellationToken ct)
    {
        for (int pos = 0; pos < length; pos += 512)
        {
            ct.ThrowIfCancellationRequested();
            try { reader.ReadAt(offset + pos, buffer, 512); }
            catch { badSectors.Add(offset + pos); }
        }
    }
}
