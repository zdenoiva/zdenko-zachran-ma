using NtfsRescue.Core;
using NtfsRescue.Util;

namespace NtfsRescue.Repair;

public sealed record ImageProgress(long BytesDone, long BytesTotal, string Phase);

public sealed class ImageResult
{
    public string Path = "";
    public long Bytes;
    public int BadRanges;
    public TimeSpan Duration;
    public readonly List<string> BadLog = new();
}

public static class DiskImageService
{
    private const int BlockSize = 4 * 1024 * 1024;

    public static async Task<ImageResult> CreateAsync(
        DiskPartition partition,
        string outputPath,
        bool wholeDisk,
        IProgress<ImageProgress>? progress,
        Action<string> log,
        CancellationToken ct)
    {
        var result = new ImageResult { Path = outputPath };
        var started = DateTime.Now;

        using var source = partition.OpenDisk();
        long sourceOffset = wholeDisk ? 0 : partition.Offset;
        long total = wholeDisk ? Math.Max(partition.DiskSize, source.Size) : partition.Length;
        if (total <= 0) throw new IOException("Nepodarilo sa zistiť veľkosť zdroja.");

        if (!partition.IsImage)
        {
            int destDisk = VolumeMapper.GetDiskNumberForPath(outputPath);
            if (destDisk >= 0 && destDisk == partition.DiskNumber)
                throw new IOException("Obraz sa nedá ukladať na ten istý fyzický disk.");
        }
        else if (Path.GetFullPath(partition.ImagePath)
                     .Equals(Path.GetFullPath(outputPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Cieľový obraz nemôže byť rovnaký ako zdrojový obraz.");
        }

        EnsureFreeSpace(outputPath, total + 128L * 1024 * 1024);

        var outputDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(outputDir)) Directory.CreateDirectory(outputDir);

        var partPath = outputPath + ".part";
        long existing = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        if (existing > total)
        {
            File.Delete(partPath);
            existing = 0;
        }

        if (existing == total)
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
            File.Move(partPath, outputPath);
            result.Bytes = total;
            result.Duration = DateTime.Now - started;
            log("Obraz už bol dokončený.");
            return result;
        }

        if (existing > 0)
            log($"Pokračujem v obraze od {Fmt.Bytes(existing)}.");

        var badRanges = new List<(long Offset, int Length)>();
        var buffer = new byte[BlockSize];

        using (var output = new FileStream(partPath, FileMode.OpenOrCreate, FileAccess.Write,
                   FileShare.None, 1 << 20, FileOptions.SequentialScan))
        {
            output.Seek(existing, SeekOrigin.Begin);
            long done = existing;

            while (done < total)
            {
                ct.ThrowIfCancellationRequested();
                int chunk = (int)Math.Min(BlockSize, total - done);
                CopyRange(source, sourceOffset + done, chunk, buffer, output, badRanges, log, ct);
                done += chunk;

                progress?.Report(new ImageProgress(done, total,
                    $"Obraz: {Fmt.Bytes(done)} / {Fmt.Bytes(total)}"));
            }

            output.Flush();
        }

        result.BadRanges = badRanges.Count;
        if (badRanges.Count > 0)
        {
            var badPath = outputPath + ".bad.txt";
            File.WriteAllLines(badPath, badRanges.Select(b =>
                $"offset={b.Offset} length={b.Length}"));
            log($"Neprečítateľné oblasti: {badRanges.Count} (zoznam: {badPath})");
            result.BadLog.AddRange(badRanges.Select(b => $"{b.Offset:N0} + {b.Length:N0} B"));
        }

        if (File.Exists(outputPath)) File.Delete(outputPath);
        File.Move(partPath, outputPath);

        result.Bytes = total;
        result.Duration = DateTime.Now - started;
        log($"Obraz hotový: {outputPath} ({Fmt.Bytes(total)})");
        return result;
    }

    private static void CopyRange(
        RawDisk source,
        long sourceOffset,
        int length,
        byte[] buffer,
        Stream output,
        List<(long Offset, int Length)> badRanges,
        Action<string> log,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            source.ReadAt(sourceOffset, buffer, length);
            output.Write(buffer, 0, length);
            return;
        }
        catch when (length > 4096)
        {
            int half = (length / 2) & ~511;
            if (half <= 0) half = length / 2;
            CopyRange(source, sourceOffset, half, buffer, output, badRanges, log, ct);
            CopyRange(source, sourceOffset + half, length - half, buffer, output, badRanges, log, ct);
            return;
        }
        catch
        {
            // Neprečítateľný sektor: do obrazu zapíšeme nuly a uložíme rozsah.
            output.Write(new byte[length], 0, length);
            badRanges.Add((sourceOffset, length));
            log($"Neprečítateľný sektor na offsete {sourceOffset:N0} ({length} B).");
        }
    }

    private static void EnsureFreeSpace(string path, long needed)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return;
            var di = new DriveInfo(root);
            if (!di.IsReady) return;
            if (di.AvailableFreeSpace < needed)
                throw new IOException(
                    $"Na disku {root} nie je dosť miesta. Potrebné aspoň {Fmt.Bytes(needed)}, " +
                    $"voľné {Fmt.Bytes(di.AvailableFreeSpace)}.");
        }
        catch (IOException) { throw; }
        catch { }
    }
}
