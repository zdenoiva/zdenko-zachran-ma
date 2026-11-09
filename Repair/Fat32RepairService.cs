using NtfsRescue.Core;
using NtfsRescue.Core.Fat32;
using NtfsRescue.Util;

namespace NtfsRescue.Repair;

public sealed class Fat32Diagnosis
{
    public DiskPartition Partition = null!;
    public Fat32BootSector? Boot;
    public Fat32BootSector? Backup;
    public bool BootValid;
    public bool BackupValid;
    public bool Fat1Valid;
    public bool Fat2Valid;
    public bool RootReadable;
    public string FatStatus = "";
    public string PrimaryError = "";
    public string BackupError = "";
}

public sealed class Fat32RepairOutcome
{
    public bool Repaired;
    public int ChkdskExit;
    public string Summary = "";
    public readonly List<(string Key, string Value)> Rows = new();
}

public static class Fat32RepairService
{
    public static Fat32Diagnosis Diagnose(DiskPartition partition, CancellationToken ct)
    {
        var result = new Fat32Diagnosis { Partition = partition };
        using var disk = partition.OpenDisk();

        var raw = disk.ReadBytes(partition.Offset, 512);
        var boot = Fat32BootSector.Parse(raw);
        result.Boot = boot;
        result.BootValid = boot.IsValid;
        result.PrimaryError = boot.Error;

        int backupSector = boot.IsValid && boot.BackupBootSector > 0 ? boot.BackupBootSector : 6;
        try
        {
            int sectorSize = boot.IsValid ? boot.BytesPerSector : partition.SectorSize;
            var backupRaw = disk.ReadBytes(partition.Offset + backupSector * (long)sectorSize, 512);
            var backup = Fat32BootSector.Parse(backupRaw);
            result.Backup = backup;
            result.BackupValid = backup.IsValid;
            result.BackupError = backup.Error;
        }
        catch (Exception ex)
        {
            result.BackupError = ex.Message;
        }

        var geometry = boot.IsValid ? boot : result.Backup;
        if (geometry is { IsValid: true })
        {
            var volume = new Fat32Volume(disk, partition.Offset, geometry);
            volume.LoadFat(ct);
            result.Fat1Valid = volume.Fat1Valid;
            result.Fat2Valid = volume.Fat2Valid;
            result.FatStatus = volume.FatStatus;

            try
            {
                using var e = volume.EnumerateDirectory((uint)geometry.RootClusterInt, ct).GetEnumerator();
                e.MoveNext();
                result.RootReadable = true;
            }
            catch
            {
                result.RootReadable = false;
            }
        }

        return result;
    }

    public static async Task<Fat32RepairOutcome> RepairAsync(
        DiskPartition partition,
        Action<string> log,
        IProgress<string>? phase,
        CancellationToken ct)
    {
        var outcome = new Fat32RepairOutcome();
        if (partition.IsImage)
        {
            outcome.Summary = "Obraz disku je len na čítanie; oprava nie je možná.";
            return outcome;
        }
        phase?.Report("Diagnostika FAT32");
        var diag = Diagnose(partition, ct);

        log($"FAT32 diagnostika: boot={(diag.BootValid ? "OK" : "chyba")}, " +
            $"záloha={(diag.BackupValid ? "OK" : "chyba")}, {diag.FatStatus}");

        outcome.Rows.Add(("boot sector", diag.BootValid ? "OK" : diag.PrimaryError));
        outcome.Rows.Add(("záložný boot", diag.BackupValid ? "OK" : diag.BackupError));
        outcome.Rows.Add(("FAT1", diag.Fat1Valid ? "OK" : "poškodená"));
        outcome.Rows.Add(("FAT2", diag.Fat2Valid ? "OK" : "poškodená"));

        bool hadLetter = partition.HasLetter;
        bool assigned = false;
        char? letter = partition.HasLetter ? partition.Letter : null;
        if (letter is null)
        {
            phase?.Report("Priraďujem dočasné písmeno");
            letter = await PartitionAccess.AssignDriveLetterAsync(partition, ct).ConfigureAwait(false);
            assigned = !hadLetter && letter is not null;
        }

        if (letter is null)
        {
            outcome.Summary = "Nepodarilo sa priradiť písmeno partícii. Oprava cez chkdsk nie je možná.";
            return outcome;
        }

        async Task<char?> EnsureLetterAsync()
        {
            if (letter is not null && Directory.Exists($"{letter}:\\")) return letter;
            partition.DriveLetter = "";
            var l = await PartitionAccess.AssignDriveLetterAsync(partition, ct).ConfigureAwait(false);
            if (l is not null && !hadLetter) assigned = true;
            return l;
        }

        try
        {
            if (!diag.BootValid && diag.BackupValid)
            {
                phase?.Report("Obnovujem boot sector");
                RestoreBootSector(partition, letter.Value, diag, log, ct);
                letter = await EnsureLetterAsync().ConfigureAwait(false) ??
                         throw new IOException("Po obnove boot sectoru sa nepodarilo obnoviť prístup k partícii.");
                diag = Diagnose(partition, ct);
            }

            if (!diag.Fat1Valid && diag.Fat2Valid)
            {
                phase?.Report("Obnovujem FAT1 z FAT2");
                RestoreFat1(partition, letter.Value, diag, log, ct);
                letter = await EnsureLetterAsync().ConfigureAwait(false) ??
                         throw new IOException("Po obnove FAT1 sa nepodarilo obnoviť prístup k partícii.");
                diag = Diagnose(partition, ct);
            }

            letter = await EnsureLetterAsync().ConfigureAwait(false) ??
                     throw new IOException("Po zápise sa nepodarilo obnoviť prístup k partícii.");

            phase?.Report("chkdsk /f (FAT32)");
            var chk = await ChkdskRunner.FixAsync(letter.Value, false, log, ct).ConfigureAwait(false);
            outcome.ChkdskExit = chk.ExitCode;
            outcome.Rows.Add(("chkdsk exit", $"{chk.ExitCode} – {ChkdskRunner.DescribeExitCode(chk.ExitCode)}"));

            await Task.Delay(1000, ct).ConfigureAwait(false);
            var verify = Diagnose(partition, ct);
            outcome.Rows.Add(("FAT1 po oprave", verify.Fat1Valid ? "OK" : "poškodená"));
            outcome.Rows.Add(("FAT2 po oprave", verify.Fat2Valid ? "OK" : "poškodená"));
            outcome.Rows.Add(("root", verify.RootReadable ? "čitateľný" : "nečitateľný"));

            outcome.Repaired = verify.BootValid && verify.RootReadable;
            outcome.Summary = outcome.Repaired
                ? "FAT32 je čitateľná a prístup k súborom funguje."
                : "FAT32 oprava sa nedokončila úplne. Skúste záchranu súborov.";
        }
        finally
        {
            if (assigned)
            {
                try { await PartitionAccess.RemoveDriveLetterAsync(partition, CancellationToken.None).ConfigureAwait(false); }
                catch { }
            }
        }

        log(outcome.Summary);
        return outcome;
    }

    public static async Task<Fat32RepairOutcome> RestoreBootOnlyAsync(
        DiskPartition partition, Action<string> log, IProgress<string>? phase, CancellationToken ct)
    {
        var outcome = new Fat32RepairOutcome();
        if (partition.IsImage)
        {
            outcome.Summary = "Obraz disku je len na čítanie; obnova boot sectoru nie je možná.";
            return outcome;
        }
        var diag = Diagnose(partition, ct);
        if (diag.BootValid)
        {
            outcome.Summary = "Primárny FAT32 boot sector je už platný.";
            outcome.Repaired = true;
            return outcome;
        }
        if (!diag.BackupValid)
        {
            outcome.Summary = "Záložný FAT32 boot sector nie je platný. Obnova nie je možná.";
            return outcome;
        }

        bool hadLetter = partition.HasLetter;
        bool assigned = false;
        char? letter = partition.HasLetter ? partition.Letter : null;
        if (letter is null)
        {
            letter = await PartitionAccess.AssignDriveLetterAsync(partition, ct).ConfigureAwait(false);
            assigned = !hadLetter && letter is not null;
        }
        if (letter is null)
        {
            outcome.Summary = "Nepodarilo sa priradiť písmeno partícii.";
            return outcome;
        }

        try
        {
            phase?.Report("Obnovujem boot sector");
            RestoreBootSector(partition, letter.Value, diag, log, ct);
            var verify = Diagnose(partition, ct);
            outcome.Repaired = verify.BootValid;
            outcome.Summary = verify.BootValid
                ? "FAT32 boot sector bol obnovený."
                : "Boot sector sa nepodarilo obnoviť.";
        }
        finally
        {
            if (assigned)
            {
                try { await PartitionAccess.RemoveDriveLetterAsync(partition, CancellationToken.None).ConfigureAwait(false); }
                catch { }
            }
        }

        log(outcome.Summary);
        return outcome;
    }

    public static async Task<Fat32RepairOutcome> RestoreFatOnlyAsync(
        DiskPartition partition, Action<string> log, IProgress<string>? phase, CancellationToken ct)
    {
        var outcome = new Fat32RepairOutcome();
        if (partition.IsImage)
        {
            outcome.Summary = "Obraz disku je len na čítanie; obnova FAT1 nie je možná.";
            return outcome;
        }
        var diag = Diagnose(partition, ct);
        if (diag.Fat1Valid)
        {
            outcome.Summary = "FAT1 je už v poriadku.";
            outcome.Repaired = true;
            return outcome;
        }
        if (!diag.Fat2Valid)
        {
            outcome.Summary = "FAT2 nie je použiteľná. Obnova FAT1 z FAT2 nie je možná.";
            return outcome;
        }

        bool hadLetter = partition.HasLetter;
        bool assigned = false;
        char? letter = partition.HasLetter ? partition.Letter : null;
        if (letter is null)
        {
            letter = await PartitionAccess.AssignDriveLetterAsync(partition, ct).ConfigureAwait(false);
            assigned = !hadLetter && letter is not null;
        }
        if (letter is null)
        {
            outcome.Summary = "Nepodarilo sa priradiť písmeno partícii.";
            return outcome;
        }

        try
        {
            phase?.Report("Obnovujem FAT1");
            RestoreFat1(partition, letter.Value, diag, log, ct);
            var verify = Diagnose(partition, ct);
            outcome.Repaired = verify.Fat1Valid;
            outcome.Summary = verify.Fat1Valid
                ? "FAT1 bola obnovená z FAT2."
                : "FAT1 sa nepodarilo obnoviť.";
        }
        finally
        {
            if (assigned)
            {
                try { await PartitionAccess.RemoveDriveLetterAsync(partition, CancellationToken.None).ConfigureAwait(false); }
                catch { }
            }
        }

        log(outcome.Summary);
        return outcome;
    }

    private static void RestoreBootSector(DiskPartition partition, char letter,
        Fat32Diagnosis diag, Action<string> log, CancellationToken ct)
    {
        int backupSector = diag.Boot is { IsValid: true } b && b.BackupBootSector > 0
            ? b.BackupBootSector
            : 6;

        using var disk = partition.OpenDisk();
        var current = disk.ReadBytes(partition.Offset, 512);
        int sectorSize = diag.Boot is { IsValid: true } bb ? bb.BytesPerSector : partition.SectorSize;
        var backup = disk.ReadBytes(partition.Offset + backupSector * (long)sectorSize, 512);
        var parsed = Fat32BootSector.Parse(backup);
        if (!parsed.IsValid)
            throw new InvalidDataException("Záložný FAT32 boot sector nie je platný: " + parsed.Error);

        Directory.CreateDirectory(AppPaths.Backups);
        var backupPath = Path.Combine(AppPaths.Backups,
            $"fat32-boot-primary-{DateTime.Now:yyyyMMdd-HHmmss}.bin");
        File.WriteAllBytes(backupPath, current);
        log("Pôvodný boot sector uložený: " + backupPath);

        // Uvoľníme raw handle, inak sa volume nedá uzamknúť.
        disk.Dispose();
        VolumeWriter.WriteAt(letter, 0, backup, log, ct);
        log("FAT32 boot sector obnovený zo zálohy.");
    }

    private static void RestoreFat1(DiskPartition partition, char letter,
        Fat32Diagnosis diag, Action<string> log, CancellationToken ct)
    {
        var boot = diag.Boot is { IsValid: true } ? diag.Boot : diag.Backup;
        if (boot is not { IsValid: true })
            throw new InvalidDataException("Chýba platný FAT32 boot sector.");

        long fatSize = boot.FatSizeBytes;
        if (fatSize <= 0 || fatSize > 512L * 1024 * 1024)
            throw new IOException($"FAT má nepodporovanú veľkosť ({Fmt.Bytes(fatSize)}).");

        using var disk = partition.OpenDisk();
        var fat1 = disk.ReadBytes(partition.Offset + boot.FatStartOffset, (int)fatSize);
        var fat2 = disk.ReadBytes(partition.Offset + boot.FatStartOffset + boot.FatSizeBytes, (int)fatSize);

        Directory.CreateDirectory(AppPaths.Backups);
        var backupPath = Path.Combine(AppPaths.Backups,
            $"fat32-fat1-{DateTime.Now:yyyyMMdd-HHmmss}.bin");
        File.WriteAllBytes(backupPath, fat1);
        log("Pôvodná FAT1 uložená: " + backupPath);

        disk.Dispose();
        VolumeWriter.WriteAt(letter, boot.FatStartOffset, fat2, log, ct);
        log("FAT1 bola obnovená z FAT2.");
    }
}

