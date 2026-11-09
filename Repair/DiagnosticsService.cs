using NtfsRescue.Core;
using NtfsRescue.Util;

namespace NtfsRescue.Repair;

public sealed class DiagnosticsResult
{
    public readonly List<(string Key, string Value)> Rows = new();
    public bool NtfsReadable;
    public bool BootFromBackup;
    public bool PrimaryBootValid;
    public bool BackupBootValid;
    public bool PrimaryBackupDiffer;
    public int? ChkdskExit;
    public string VolumeStatus = "";
    public string VolumeHealth = "";
    public VolumeLocation Location = new(-1, 0, 0, 512, 0, "", "", 0);
    public long TotalRecords;
    public bool? Dirty;
    public string Summary = "";
}

public static class DiagnosticsService
{
    public static async Task<DiagnosticsResult> RunAsync(
        VolumeEntry entry,
        bool runChkdsk,
        Action<string> log,
        IProgress<string>? phase,
        CancellationToken ct)
    {
        var result = new DiagnosticsResult();
        char letter = entry.Letter;

        void Row(string key, string value)
        {
            result.Rows.Add((key, value));
            log($"{key}: {value}");
        }

        Row("Volume", $"{letter}:  {entry.Label}");
        Row("Súborový systém", string.IsNullOrWhiteSpace(entry.FileSystem) ? "nezistený" : entry.FileSystem);
        Row("Stav", string.IsNullOrWhiteSpace(entry.Status) ? entry.Health : $"{entry.Health} / {entry.Status}");
        Row("Veľkosť", entry.Size > 0 ? Fmt.Bytes(entry.Size) : "neznáma");

        phase?.Report("Zisťujem fyzický disk");
        var loc = VolumeMapper.Locate(letter);
        result.Location = loc;

        if (loc.IsValid)
        {
            Row("Fyzický disk", $"#{loc.DiskNumber}  {loc.Model}".Trim());
            if (!string.IsNullOrWhiteSpace(loc.Serial)) Row("Sériové číslo disku", loc.Serial);
            Row("Offset partície", $"{loc.Offset:N0} B");
            Row("Dĺžka partície", Fmt.Bytes(loc.Length));
            Row("Sektor", $"{loc.SectorSize} B");
            Row("Veľkosť disku", loc.DiskSize > 0 ? Fmt.Bytes(loc.DiskSize) : "neznáma");
        }
        else
        {
            Row("Fyzický disk", "nepodarilo sa zistiť (spustite ako administrátor)");
        }

        try
        {
            phase?.Report("Čítam NTFS štruktúru");
            using var disk = new RawDisk(loc.DiskNumber, loc.SectorSize, loc.DiskSize);
            var volume = NtfsVolume.Open(disk, loc.Offset, loc.Length);

            result.NtfsReadable = true;
            result.BootFromBackup = volume.BootFromBackup;
            result.PrimaryBootValid = volume.PrimaryBoot?.IsValid == true;
            result.BackupBootValid = volume.BackupBoot?.IsValid == true;
            result.TotalRecords = volume.TotalRecords;

            Row("Boot sector", volume.Boot.Describe());
            Row("Primárny boot sector", volume.PrimaryBoot?.IsValid == true ? "OK" : volume.PrimaryBoot?.Error ?? "nečitateľný");
            Row("Záložný boot sector", volume.BackupBoot?.IsValid == true ? "OK" : volume.BackupBoot?.Error ?? "nečitateľný");

            if (volume.PrimaryBoot is { IsValid: true } && volume.BackupBoot is { IsValid: true })
            {
                var cmp = volume.PrimaryBoot.Compare(volume.BackupBoot);
                Row("Porovnanie boot sectorov", cmp);
                result.PrimaryBackupDiffer = cmp != "rovnaké";
            }

            Row("Záznamov v $MFT", volume.TotalRecords.ToString("N0"));
            Row("Veľkosť $MFT", Fmt.Bytes(volume.MftRealSize));

            var root = volume.ReadRecordCached(5);
            Row("Koreňový adresár ($MFT 5)", root is { IsDirectory: true } ? "OK" : "poškodený alebo nečitateľný");

            var bitmap = volume.ReadRecordCached(6);
            Row("$Bitmap (MFT 6)", bitmap is not null ? "OK" : "nečitateľný");

            var secure = volume.ReadRecordCached(9);
            Row("$Secure (MFT 9)", secure is not null ? "OK" : "nečitateľný");

            if (volume.BootFromBackup)
                Row("Pozor", "primárny boot sector je poškodený, dá sa obnoviť zo zálohy");
        }
        catch (Exception ex)
        {
            log("NTFS štruktúru nebolo možné prečítať: " + ex.Message);
            Row("NTFS štruktúra", "nečitateľná: " + ex.Message);
        }

        phase?.Report("Kontrolujem dirty bit");
        result.Dirty = await QueryDirtyAsync(letter, log, ct).ConfigureAwait(false);
        if (result.Dirty.HasValue)
            Row("Dirty bit", result.Dirty.Value ? "Dirty (nekonzistentný)" : "čistý");

        if (runChkdsk)
        {
            phase?.Report("chkdsk (len čítanie)");
            log("");
            log("=== chkdsk (len čítanie) ===");
            try
            {
                var chk = await ChkdskRunner.ReadOnlyAsync(letter, line => log(line), ct).ConfigureAwait(false);
                result.ChkdskExit = chk.ExitCode;
                Row("chkdsk exit", $"{chk.ExitCode} – {ChkdskRunner.DescribeExitCode(chk.ExitCode)}");
                Row("chkdsk výsledok", chk.Output.Contains("Errors found", StringComparison.OrdinalIgnoreCase)
                    ? "nájdené chyby"
                    : chk.ExitCode == 0 ? "bez chýb" : "pozri protokol");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log("chkdsk zlyhal: " + ex.Message);
                Row("chkdsk", "nepodarilo sa spustiť: " + ex.Message);
            }
        }

        phase?.Report("Obnovujem stav volume");
        try
        {
            var fresh = await VolumeCatalog.GetOneAsync(letter, ct).ConfigureAwait(false);
            if (fresh is not null)
            {
                result.VolumeStatus = fresh.Status;
                result.VolumeHealth = fresh.Health;
                Row("Aktuálny stav Windows", $"{fresh.Health} / {fresh.Status}");
            }
        }
        catch { }

        result.Summary = result.NtfsReadable
            ? "NTFS sa dá prečítať. " + (result.Dirty == true ? "Volume je označený ako dirty. " : "")
              + (result.PrimaryBackupDiffer ? "Boot sector a jeho záloha sa líšia. " : "")
            : "NTFS štruktúru nebolo možné prečítať.";

        log("");
        log("Zhrnutie: " + result.Summary);
        return result;
    }

    public static async Task<bool?> QueryDirtyAsync(char letter, Action<string>? log, CancellationToken ct)
    {
        try
        {
            var r = await ProcessRunner.RunAsync(
                Path.Combine(Environment.SystemDirectory, "fsutil.exe"),
                new[] { "dirty", "query", $"{letter}:" },
                null, ct, 30000).ConfigureAwait(false);

            if (r.ExitCode != 0) return null;
            var text = r.Output.Trim();
            if (text.Length == 0) return null;
            if (text.Contains("is not dirty", StringComparison.OrdinalIgnoreCase)) return false;
            if (text.Contains("is dirty", StringComparison.OrdinalIgnoreCase)) return true;
            if (text.Contains("dirty", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
        catch (Exception ex)
        {
            log?.Invoke("Dirty bit sa nepodarilo zistiť: " + ex.Message);
            return null;
        }
    }
}
