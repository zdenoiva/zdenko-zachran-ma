using System.Diagnostics;
using NtfsRescue.Core;
using NtfsRescue.Util;

namespace NtfsRescue.Repair;

public sealed record EraseProgress(long BytesDone, long BytesTotal, string Phase, double BytesPerSecond);

public sealed class EraseOptions
{
    public bool FullWipe;
    public bool CreatePartition = true;
    public string FileSystem = "NTFS";
    public string PartitionStyle = "GPT";
    public char? Letter;
    public string Label = "DATA";
}

public sealed class EraseResult
{
    public bool Success;
    public string Summary = "";
    public string DriveLetter = "";
}

public static class DiskEraser
{
    private const int BlockSize = 4 * 1024 * 1024;
    private const long TwoTeraBytes = 2_199_023_255_552;

    public static async Task<EraseResult> EraseAsync(DiskInfo disk, EraseOptions options,
        IProgress<EraseProgress>? progress, Action<string> log, CancellationToken ct)
    {
        if (disk.IsCurrentSystem)
            throw new InvalidOperationException("Aktuálny systémový disk nie je možné vymazať.");
        if (disk.Size <= 0)
            throw new InvalidOperationException("Neznáma veľkosť disku.");

        var appDisk = VolumeMapper.GetDiskNumberForPath(Environment.ProcessPath ?? "");
        if (appDisk >= 0 && appDisk == disk.Number)
            throw new InvalidOperationException("Program beží z tohto disku. Spustite ho z iného disku.");

        if (options.PartitionStyle.Equals("MBR", StringComparison.OrdinalIgnoreCase) &&
            disk.Size > TwoTeraBytes)
            throw new InvalidOperationException("Disk nad 2 TB nie je možné inicializovať ako MBR.");

        if (options.Letter is { } letter && IsLetterUsed(letter, disk.Number))
            throw new InvalidOperationException($"Písmeno {letter}: je už použité.");

        if (options.FullWipe)
        {
            log($"=== ÚPLNÉ PREPÍSANIE NULAMI: Disk {disk.Number} · {disk.Model} · {Fmt.Bytes(disk.Size)} ===");
            await WipeRawAsync(disk, progress, log, ct).ConfigureAwait(false);
            log("Disk bol prepísaný nulami.");
        }
        else
        {
            log($"=== ODSTRÁNENIE PARTÍCIÍ: Disk {disk.Number} · {disk.Model} · {Fmt.Bytes(disk.Size)} ===");
            await RunPowerShellAsync($"""
$ErrorActionPreference = 'Stop'
Set-Disk -Number {disk.Number} -IsOffline $false -ErrorAction SilentlyContinue
Set-Disk -Number {disk.Number} -IsReadOnly $false -ErrorAction SilentlyContinue
Clear-Disk -Number {disk.Number} -RemoveData -RemoveOEM -Confirm:$false -ErrorAction Stop
""", log, ct).ConfigureAwait(false);
            log("Partície a dátové štruktúry boli odstránené.");
        }

        string drive = "";
        if (options.CreatePartition)
        {
            drive = await CreatePartitionAsync(disk, options, log, ct).ConfigureAwait(false);
            log($"Vytvorená partícia {options.FileSystem} {(string.IsNullOrWhiteSpace(drive) ? "(bez písmena)" : drive + ":")}");
        }
        else
        {
            await RunPowerShellAsync($"""
$ErrorActionPreference = 'Stop'
Set-Disk -Number {disk.Number} -IsOffline $false -ErrorAction SilentlyContinue
""", log, ct).ConfigureAwait(false);
        }

        return new EraseResult
        {
            Success = true,
            DriveLetter = drive,
            Summary = options.FullWipe
                ? "Disk bol prepísaný nulami."
                : "Partície boli odstránené.",
            };
    }

    private static async Task WipeRawAsync(DiskInfo disk, IProgress<EraseProgress>? progress,
        Action<string> log, CancellationToken ct)
    {
        await RunPowerShellAsync($"""
$ErrorActionPreference = 'SilentlyContinue'
Set-Disk -Number {disk.Number} -IsReadOnly $false
Set-Disk -Number {disk.Number} -IsOffline $false
Set-Disk -Number {disk.Number} -IsOffline $true
""", log, ct).ConfigureAwait(false);

        var device = $@"\\.\PhysicalDrive{disk.Number}";
        log($"Otváram {device} pre zápis…");

        try
        {
            using var stream = new FileStream(device, FileMode.Open, FileAccess.Write,
                FileShare.ReadWrite, BlockSize, FileOptions.SequentialScan);
            var buffer = new byte[BlockSize];
            long total = disk.Size;
            long done = 0;
            var stopwatch = Stopwatch.StartNew();
            var lastReport = 0L;

            try
            {
                while (done < total)
                {
                    ct.ThrowIfCancellationRequested();
                    int count = (int)Math.Min(BlockSize, total - done);
                    await stream.WriteAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
                    done += count;

                    if (stopwatch.ElapsedMilliseconds - lastReport >= 500 || done == total)
                    {
                        lastReport = stopwatch.ElapsedMilliseconds;
                        var speed = stopwatch.Elapsed.TotalSeconds > 0
                            ? done / stopwatch.Elapsed.TotalSeconds
                            : 0;
                        progress?.Report(new EraseProgress(done, total,
                            $"Prepísané {Fmt.Bytes(done)} / {Fmt.Bytes(total)}", speed));
                    }
                }

                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(true);
            }
            finally
            {
                try
                {
                    await RunPowerShellAsync($"""
$ErrorActionPreference = 'SilentlyContinue'
Set-Disk -Number {disk.Number} -IsOffline $false
""", log, CancellationToken.None).ConfigureAwait(false);
                }
                catch { }
            }

            log("Verifikujem prvé a posledné sektory…");
            using var raw = new RawDisk(disk.Number, 512, disk.Size);
            byte[]? first = null;
            byte[]? last = null;
            try { first = raw.ReadBytes(0, 4096); }
            catch (Exception ex) { log("Začiatok disku sa nepodarilo overiť: " + ex.Message); }
            try { last = raw.ReadBytes(Math.Max(0, total - 4096), 4096); }
            catch (Exception ex) { log("Koniec disku sa nepodarilo overiť: " + ex.Message); }

            if ((first is not null && first.Any(b => b != 0)) ||
                (last is not null && last.Any(b => b != 0)))
                throw new InvalidOperationException("Verifikácia zlyhala: na disku zostali nenulové dáta.");

            log(first is not null && last is not null
                ? "Verifikácia: prvé aj posledné sektory sú nulové."
                : "Zápis prebehol; časť overenia nebola dostupná.");
        }
        catch (OperationCanceledException)
        {
            log("Prepísanie bolo zrušené. Disk je čiastočne prepísaný.");
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException("Disk sa nepodarilo otvoriť na zápis. Je disk v poriadku a máte administrátorské práva?", ex);
        }
    }

    private static async Task<string> CreatePartitionAsync(DiskInfo disk, EraseOptions options,
        Action<string> log, CancellationToken ct)
    {
        var script = new System.Text.StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine($"Set-Disk -Number {disk.Number} -IsOffline $false -ErrorAction SilentlyContinue");
        script.AppendLine($"Set-Disk -Number {disk.Number} -IsReadOnly $false -ErrorAction SilentlyContinue");
        script.AppendLine($"Initialize-Disk -Number {disk.Number} -PartitionStyle {options.PartitionStyle} -Confirm:$false -ErrorAction Stop");

        string partition;
        if (options.Letter is { } letter)
            partition = $"$p = New-Partition -DiskNumber {disk.Number} -UseMaximumSize -DriveLetter {letter} -ErrorAction Stop";
        else
            partition = $"$p = New-Partition -DiskNumber {disk.Number} -UseMaximumSize -AssignDriveLetter -ErrorAction Stop";
        script.AppendLine(partition);

        var fs = options.FileSystem.Equals("FAT32", StringComparison.OrdinalIgnoreCase) ? "FAT32" : "NTFS";
        var label = options.Label.Replace("'", "''");
        script.AppendLine($"$letter = [string]$p.DriveLetter");
        script.AppendLine($"Format-Volume -Partition $p -FileSystem {fs} -NewFileSystemLabel '{label}' -Confirm:$false -Force -ErrorAction Stop | Out-Null");
        script.AppendLine("if (-not $letter) { $letter = [string](Get-Partition -DiskNumber " + disk.Number +
                          " -PartitionNumber $p.PartitionNumber).DriveLetter }");
        script.AppendLine("[string]$letter");

        var output = await RunPowerShellAsync(script.ToString(), log, ct).ConfigureAwait(false);
        var line = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault()?.Trim() ?? "";
        return line.Length == 1 && line[0] is >= 'A' and <= 'Z' ? line : "";
    }

    private static bool IsLetterUsed(char letter, int targetDisk)
    {
        try
        {
            letter = char.ToUpperInvariant(letter);
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (string.IsNullOrEmpty(drive.Name)) continue;
                if (char.ToUpperInvariant(drive.Name[0]) != letter) continue;

                // Písmeno na cieľovom disku sa po vyčistení uvoľní, preto ho
                // môžeme pre novú partíciu znova použiť.
                var location = VolumeMapper.Locate(letter);
                if (location.IsValid && location.DiskNumber == targetDisk)
                    continue;
                return true;
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static async Task<string> RunPowerShellAsync(string script, Action<string> log,
        CancellationToken ct)
    {
        var powershell = Path.Combine(Environment.SystemDirectory,
            "WindowsPowerShell", "v1.0", "powershell.exe");
        var result = await ProcessRunner.RunAsync(powershell,
            new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script },
            log, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                "Operácia zlyhala (PowerShell exit " + result.ExitCode + "). Pozrite protokol.");
        return result.Output;
    }
}
