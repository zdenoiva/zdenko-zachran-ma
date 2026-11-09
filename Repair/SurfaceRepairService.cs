using System.Diagnostics;
using System.Text;
using NtfsRescue.Core;
using NtfsRescue.Util;

namespace NtfsRescue.Repair;

public sealed record SurfaceRepairProgress(
    int Attempted,
    int Total,
    int Repaired,
    int Failed,
    long Offset,
    string Phase);

public sealed class SurfaceRepairResult
{
    public int Attempted;
    public int Repaired;
    public int Failed;
    public string BackupPath = "";
    public string Summary = "";
}

public static class SurfaceRepairService
{
    public static async Task<SurfaceRepairResult> RepairAsync(
        DiskInfo disk,
        IReadOnlyList<long> badSectors,
        IProgress<SurfaceRepairProgress>? progress,
        Action<string> log,
        CancellationToken ct)
    {
        if (disk.IsCurrentSystem)
            throw new InvalidOperationException("Aktuálny systémový disk nie je možné opravovať zápisom.");
        var appDisk = VolumeMapper.GetDiskNumberForPath(Environment.ProcessPath ?? "");
        if (appDisk >= 0 && appDisk == disk.Number)
            throw new InvalidOperationException("Program beží z tohto disku. Spustite ho z iného disku.");

        var result = new SurfaceRepairResult();
        if (badSectors.Count == 0)
        {
            result.Summary = "Nie sú evidované žiadne vadné sektory.";
            return result;
        }

        result.BackupPath = CreateBackupPath();
        log($"Záloha čitateľných dát: {result.BackupPath}");
        log("Disk sa dočasne odpojí, aby zápis neblokovali pripojené volumes.");

        var offline = await TrySetOfflineAsync(disk.Number, true, log, ct).ConfigureAwait(false);
        try
        {
            var device = $@"\\.\PhysicalDrive{disk.Number}";
            using var stream = new FileStream(device, FileMode.Open, FileAccess.ReadWrite,
                FileShare.ReadWrite, 4096, FileOptions.None);

            var sector = new byte[512];
            var zero = new byte[512];
            var backup = new StringBuilder();
            int attempted = 0;

            foreach (var offset in badSectors.Distinct().OrderBy(x => x))
            {
                ct.ThrowIfCancellationRequested();
                attempted++;
                bool readOk = TryRead(stream, offset, sector);
                if (readOk)
                {
                    backup.AppendLine($"{offset}\t{Convert.ToHexString(sector)}");
                    log($"Sektor {offset:N0}: čitateľný, ukladám kópiu a zapisujem späť.");
                }
                else
                {
                    log($"Sektor {offset:N0}: nečitateľný, zapisujem nuly (dáta sektora sa nedajú zachrániť).");
                }

                try
                {
                    stream.Position = offset;
                    stream.Write(readOk ? sector : zero, 0, 512);
                    stream.Flush(true);
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    log($"Sektor {offset:N0}: zápis zlyhal: {ex.Message}");
                    progress?.Report(new SurfaceRepairProgress(attempted, badSectors.Count,
                        result.Repaired, result.Failed, offset, "Zápis zlyhal"));
                    continue;
                }

                if (TryRead(stream, offset, sector))
                {
                    result.Repaired++;
                    log($"Sektor {offset:N0}: po zápise čitateľný.");
                }
                else
                {
                    result.Failed++;
                    log($"Sektor {offset:N0}: po zápise stále nečitateľný.");
                }

                progress?.Report(new SurfaceRepairProgress(attempted, badSectors.Count,
                    result.Repaired, result.Failed, offset, "Opravujem sektory"));
            }

            if (backup.Length > 0)
                await File.WriteAllTextAsync(result.BackupPath, backup.ToString(), Encoding.UTF8, ct)
                    .ConfigureAwait(false);
        }
        finally
        {
            if (offline)
                await TrySetOfflineAsync(disk.Number, false, log, CancellationToken.None)
                    .ConfigureAwait(false);
        }

        result.Attempted = badSectors.Count;
        result.Summary =
            $"Oprava povrchu: {result.Repaired:N0} sektorov čitateľných po zápise, " +
            $"{result.Failed:N0} stále chybných.";
        log(result.Summary);
        return result;
    }

    private static bool TryRead(FileStream stream, long offset, byte[] buffer)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                stream.Position = offset;
                int read = 0;
                while (read < buffer.Length)
                {
                    int n = stream.Read(buffer, read, buffer.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read == buffer.Length) return true;
            }
            catch { }
            Thread.Sleep(80);
        }
        return false;
    }

    private static async Task<bool> TrySetOfflineAsync(int diskNumber, bool offline,
        Action<string> log, CancellationToken ct)
    {
        try
        {
            var value = offline ? "$true" : "$false";
            var script =
                $"$ErrorActionPreference = 'Stop'; " +
                $"Set-Disk -Number {diskNumber} -IsOffline {value} -ErrorAction Stop";
            var powershell = Path.Combine(Environment.SystemDirectory,
                "WindowsPowerShell", "v1.0", "powershell.exe");
            var result = await ProcessRunner.RunAsync(powershell,
                new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script },
                null, ct, 60000).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                log($"Disk sa nepodarilo {(offline ? "odpojiť" : "pripojiť")}; pokračujem online.");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            log("Zmena offline stavu zlyhala: " + ex.Message);
            return false;
        }
    }

    private static string CreateBackupPath()
    {
        var dir = Path.Combine(AppPaths.Base, "surface-backup");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"bad-sectors-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
    }
}
