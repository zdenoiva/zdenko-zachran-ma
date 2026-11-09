using System.Text;
using NtfsRescue.Util;

namespace NtfsRescue.Core;

public sealed record CopyProgress(
    string CurrentFile,
    long BytesDone,
    long BytesTotal,
    int FilesDone,
    int FilesTotal);

public sealed class CopyReport
{
    public int Copied;
    public int Skipped;
    public int Failed;
    public long Bytes;
    public readonly List<string> Errors = new();
    public DateTime Started = DateTime.Now;
    public DateTime Finished = DateTime.Now;
    public TimeSpan Duration => Finished - Started;
}

public sealed class CopyOptions
{
    public string DestinationRoot = "";
    public bool Overwrite;
    public bool SkipExistingSameSize = true;
    public bool VerifySize = true;
}

public static class RecoveryCopier
{
    public static async Task<CopyReport> CopyAsync(
        IRecoverySource source,
        IReadOnlyList<RecoveryEntry> files,
        CopyOptions options,
        IProgress<CopyProgress>? progress,
        Action<string>? onMessage,
        CancellationToken ct)
    {
        var report = new CopyReport { Started = DateTime.Now };
        var root = Path.GetFullPath(options.DestinationRoot);

        int destDisk = VolumeMapper.GetDiskNumberForPath(root);
        if (destDisk >= 0 && destDisk == source.DiskNumber)
            throw new IOException("Cieľový priečinok je na tom istom fyzickom disku ako zdroj. Vyberte iný disk.");

        Directory.CreateDirectory(root);

        var list = files.Where(f => !f.IsDirectory).ToList();
        long totalBytes = list.Sum(f => Math.Max(0, f.Size));
        if (totalBytes > 0) CheckFreeSpace(root, totalBytes);
        int doneFiles = 0;
        long doneBytes = 0;

        progress?.Report(new CopyProgress("", 0, totalBytes, 0, list.Count));

        foreach (var file in list)
        {
            ct.ThrowIfCancellationRequested();

            var relative = SanitizeRelativePath(file.FullPath, 0);
            var full = Path.Combine(root, relative);
            var dir = Path.GetDirectoryName(full);
            string? temp = null;

            try
            {
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(ToLongPath(dir));

                if (File.Exists(ToLongPath(full)))
                {
                    var existing = new FileInfo(ToLongPath(full));
                    if (options.SkipExistingSameSize && file.Size > 0 && existing.Length == file.Size)
                    {
                        report.Skipped++;
                        doneFiles++;
                        doneBytes += file.Size;
                        onMessage?.Invoke($"Preskočené (už existuje): {relative}");
                        progress?.Report(new CopyProgress(relative, doneBytes, totalBytes, doneFiles, list.Count));
                        continue;
                    }

                    if (!options.Overwrite)
                        full = MakeUnique(full);
                }

                temp = full + ".zdenko-part";
                if (File.Exists(ToLongPath(temp))) File.Delete(ToLongPath(temp));

                long fileDone = 0;
                var lastProgress = DateTime.MinValue;
                using (var output = new FileStream(ToLongPath(temp), FileMode.Create, FileAccess.Write,
                           FileShare.None, 1 << 20, FileOptions.SequentialScan))
                {
                    source.CopyFile(file, output, written =>
                    {
                        fileDone = written;
                        var now = DateTime.UtcNow;
                        if ((now - lastProgress).TotalMilliseconds >= 250)
                        {
                            lastProgress = now;
                            progress?.Report(new CopyProgress(relative, doneBytes + written, totalBytes, doneFiles, list.Count));
                        }
                    }, ct, msg => onMessage?.Invoke($"{relative}: {msg}"));

                    output.Flush();
                }

                var fi = new FileInfo(ToLongPath(temp));
                if (options.VerifySize)
                {
                    if (file.Size > 0 && fi.Length != file.Size)
                        throw new IOException($"veľkosť po zápise nesedí ({fi.Length} != {file.Size})");

                    if (file.Size <= 0)
                        onMessage?.Invoke($"{relative}: veľkosť neznáma (0), kontrola veľkosti preskočená; " +
                                         $"zapísaných {fi.Length} B.");
                }

                if (File.Exists(ToLongPath(full)))
                {
                    try { File.Replace(ToLongPath(temp), ToLongPath(full), null); }
                    catch
                    {
                        File.Delete(ToLongPath(full));
                        File.Move(ToLongPath(temp), ToLongPath(full));
                    }
                }
                else
                {
                    File.Move(ToLongPath(temp), ToLongPath(full));
                }
                temp = null;

                report.Copied++;
                report.Bytes += fi.Length;
                doneFiles++;
                doneBytes += fi.Length;
                onMessage?.Invoke($"OK: {relative} ({Fmt.Bytes(fi.Length)})");
                progress?.Report(new CopyProgress(relative, doneBytes, totalBytes, doneFiles, list.Count));
            }
            catch (OperationCanceledException)
            {
                if (temp is not null)
                    try { if (File.Exists(ToLongPath(temp))) File.Delete(ToLongPath(temp)); } catch { }
                throw;
            }
            catch (Exception ex)
            {
                if (temp is not null)
                    try { if (File.Exists(ToLongPath(temp))) File.Delete(ToLongPath(temp)); } catch { }

                report.Failed++;
                report.Errors.Add($"{relative}: {ex.Message}");
                onMessage?.Invoke($"CHYBA: {relative}: {ex.Message}");
                doneFiles++;
            }
        }

        report.Finished = DateTime.Now;
        return report;
    }

    public static string SanitizeRelativePath(string path, ulong recordNumber)
    {
        var parts = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeSegment)
            .Where(p => p.Length > 0)
            .ToList();

        if (parts.Count == 0) parts.Add($"record_{recordNumber}");
        return Path.Combine(parts.ToArray());
    }

    public static string SanitizeSegment(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (c < 32 || c is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
                sb.Append('_');
            else
                sb.Append(c);
        }

        var result = sb.ToString().TrimEnd(' ', '.');
        if (result.Length == 0) result = "_";

        var reserved = new[]
        {
            "CON","PRN","AUX","NUL","COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
            "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
        };
        var stem = Path.GetFileNameWithoutExtension(result);
        if (reserved.Any(r => r.Equals(stem, StringComparison.OrdinalIgnoreCase)))
            result = "_" + result;

        if (result.Length > 200) result = result[..200];
        return result;
    }

    public static string ToLongPath(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return @"\\?\UNC\" + path[2..];
        return @"\\?\" + path;
    }

    private static string MakeUnique(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (int i = 1; i < 10000; i++)
        {
            var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(ToLongPath(candidate)) && !Directory.Exists(ToLongPath(candidate)))
                return candidate;
        }

        return Path.Combine(dir, $"{name} ({DateTime.Now:HHmmssfff}){ext}");
    }

    private static void CheckFreeSpace(string root, long needed)
    {
        try
        {
            var rootPath = Path.GetPathRoot(root);
            if (string.IsNullOrEmpty(rootPath)) return;

            var di = new DriveInfo(rootPath);
            if (!di.IsReady) return;

            long reserve = 128L * 1024 * 1024;
            if (di.AvailableFreeSpace < needed + reserve)
                throw new IOException(
                    $"Na cieľovom disku {rootPath} nie je dosť miesta. Potrebné aspoň " +
                    $"{Fmt.Bytes(needed + reserve)}, voľné {Fmt.Bytes(di.AvailableFreeSpace)}.");
        }
        catch (IOException) { throw; }
        catch { }
    }
}
