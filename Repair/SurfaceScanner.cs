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
    int LastBlockIndex,
    long Position);

public sealed class SurfaceScanResult
{
    public readonly List<SurfaceBlock> Blocks = new();
    public readonly List<long> BadSectors = new();
    public int BadBlocks;
    public int SlowBlocks;
    public long TotalBytes;
    public TimeSpan Duration;
    public int FirstBlock;
    public int LastBlock;
    public int FullBlocks;
    public string Summary = "";

    public bool IsFullRange => FirstBlock == 0 && LastBlock == FullBlocks - 1;

    public void SaveReport(string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("offset,length,ms,status");
        foreach (var block in Blocks)
        {
            var status = block.Bad ? "CHYBA" : block.Ms >= SurfaceScanner.VerySlowMs ? "VELMI_POMALE"
                : block.Ms >= SurfaceScanner.SlowMs ? "POMALE"
                : block.Ms >= SurfaceScanner.MediumMs ? "STRIEDAVE" : "OK";
            sb.AppendLine($"{block.Offset},{block.Length}," +
                          $"{block.Ms.ToString("F1", CultureInfo.InvariantCulture)},{status}");
        }
        sb.AppendLine();
        sb.AppendLine("# vadne sektory");
        foreach (var offset in BadSectors) sb.AppendLine(offset.ToString(CultureInfo.InvariantCulture));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }
}

public static class SurfaceScanner
{
    public const int BlockSize = 4 * 1024 * 1024;
    public const double MediumMs = 50;
    public const double SlowMs = 100;
    public const double VerySlowMs = 250;

    public static int BlockCount(long size)
        => (int)Math.Min(int.MaxValue, (size + BlockSize - 1) / BlockSize);

    /// <summary>Stav bloku pre mapu: 1 = OK, 2 = 50+ ms, 3 = 100+ ms, 4 = 250+ ms, 5 = chyba.</summary>
    public static int Severity(SurfaceBlock block)
        => block.Bad ? 5
            : block.Ms >= VerySlowMs ? 4
            : block.Ms >= SlowMs ? 3
            : block.Ms >= MediumMs ? 2
            : 1;

    public static SurfaceScanResult Scan(DiskInfo disk, int firstBlock, int lastBlock,
        IProgress<SurfaceProgress>? progress, Action<string>? log, CancellationToken ct,
        Action<int, SurfaceBlock>? onBlock = null, Action<long>? onBadSector = null)
    {
        if (disk.Size <= 0) throw new InvalidOperationException("Neznáma veľkosť disku.");

        var fullBlocks = BlockCount(disk.Size);
        if (fullBlocks <= 0) throw new InvalidOperationException("Neznáma veľkosť disku.");
        firstBlock = Math.Clamp(firstBlock, 0, fullBlocks - 1);
        lastBlock = Math.Clamp(lastBlock, firstBlock, fullBlocks - 1);

        long startOffset = (long)firstBlock * BlockSize;
        long endOffset = Math.Min(disk.Size, (long)(lastBlock + 1) * BlockSize);
        long rangeBytes = endOffset - startOffset;
        int rangeBlocks = lastBlock - firstBlock + 1;

        var result = new SurfaceScanResult
        {
            TotalBytes = rangeBytes,
            FirstBlock = firstBlock,
            LastBlock = lastBlock,
            FullBlocks = fullBlocks
        };
        var badSectors = new HashSet<long>();
        var buffer = new byte[BlockSize];
        var sectorBuffer = new byte[512];
        var stopwatch = Stopwatch.StartNew();
        long lastReport = -1000;

        using var reader = new DirectDiskReader(disk.Number);
        if (!reader.Direct)
            log?.Invoke("Priame čítanie nie je dostupné; používam bežné čítanie (môže byť ovplyvnené cache).");

        long done = 0;
        for (int i = 0; i < rangeBlocks; i++)
        {
            ct.ThrowIfCancellationRequested();
            int index = firstBlock + i;
            long offset = (long)index * BlockSize;
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
                CheckBadSectors(reader, offset, length, sectorBuffer, badSectors, ct, onBadSector);
            }

            if (!block.Bad && block.Ms >= SlowMs) result.SlowBlocks++;
            result.Blocks.Add(block);
            onBlock?.Invoke(index, block);
            done += length;

            if (stopwatch.ElapsedMilliseconds - lastReport >= 500 || i == rangeBlocks - 1)
            {
                lastReport = stopwatch.ElapsedMilliseconds;
                var speed = stopwatch.Elapsed.TotalSeconds > 0 ? done / stopwatch.Elapsed.TotalSeconds : 0;
                var eta = speed > 0 ? TimeSpan.FromSeconds(Math.Max(0, (rangeBytes - done) / speed)) : TimeSpan.Zero;
                progress?.Report(new SurfaceProgress(done, rangeBytes, i + 1, rangeBlocks, speed,
                    stopwatch.Elapsed, eta, index, offset));
            }
        }

        result.BadSectors.AddRange(badSectors.OrderBy(x => x));
        result.Duration = stopwatch.Elapsed;
        var range = result.IsFullRange
            ? $"celý disk {Fmt.Bytes(disk.Size)}"
            : $"bloky {firstBlock:N0}–{lastBlock:N0} z {fullBlocks:N0} · {Fmt.Bytes(rangeBytes)}";
        result.Summary =
            $"Povrch: {range} · {result.BadBlocks:N0} chybných blokov · " +
            $"{result.BadSectors.Count:N0} vadných sektorov · " +
            $"{result.SlowBlocks:N0} pomalých blokov · {result.Duration:hh\\:mm\\:ss}";
        log?.Invoke(result.Summary);
        return result;
    }

    private static void CheckBadSectors(DirectDiskReader reader, long offset, int length, byte[] buffer,
        HashSet<long> badSectors, CancellationToken ct, Action<long>? onBadSector)
    {
        for (int pos = 0; pos < length; pos += 512)
        {
            ct.ThrowIfCancellationRequested();
            try { reader.ReadAt(offset + pos, buffer, 512); }
            catch
            {
                var sector = offset + pos;
                if (badSectors.Add(sector)) onBadSector?.Invoke(sector);
            }
        }
    }
}
