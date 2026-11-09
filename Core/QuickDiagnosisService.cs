using NtfsRescue.Core;
using NtfsRescue.Core.Fat32;
using NtfsRescue.Repair;
using NtfsRescue.Util;

namespace NtfsRescue.Core;

public enum DiagnosisSeverity
{
    Ok,
    Info,
    Warning,
    Critical
}

public enum RecommendedAction
{
    CopyFiles,
    Repair,
    DeepScan,
    RestoreBoot,
    RestoreFat,
    AssignLetter,
    TryNtfs,
    TryFat32
}

public sealed class QuickDiagnosisReport
{
    public DiskPartition Partition = null!;
    public FileSystemKind FileSystem;
    public DiagnosisSeverity Severity;
    public string Title = "";
    public string Details = "";
    public string SmartText = "";
    public bool SmartAlert;
    public bool SmartAvailable;
    public readonly List<RecommendedAction> Actions = new();
    public bool HasLetter;
    public bool BootFromBackup;
    public bool NtfsReadable;
    public bool FatBootValid;
    public bool FatBackupValid;
    public bool Fat1Valid;
    public bool Fat2Valid;
}

public static class QuickDiagnosisService
{
    public static async Task<QuickDiagnosisReport> RunAsync(DiskPartition partition, CancellationToken ct)
    {
        return await Task.Run(() => Diagnose(partition, ct), ct).ConfigureAwait(false);
    }

    private static QuickDiagnosisReport Diagnose(DiskPartition partition, CancellationToken ct)
    {
        var report = new QuickDiagnosisReport
        {
            Partition = partition,
            FileSystem = partition.FileSystem,
            HasLetter = partition.HasLetter
        };

        if (partition.IsImage)
        {
            report.SmartText = "S.M.A.R.T: obraz – nedostupné";
            report.SmartAvailable = false;
        }
        else if (partition.DiskNumber >= 0)
        {
            var smart = SmartInfoService.Query(partition.DiskNumber, ct);
            report.SmartText = smart.DisplayText;
            report.SmartAlert = smart.HasAlert;
            report.SmartAvailable = smart.Available;
        }

        switch (partition.FileSystem)
        {
            case FileSystemKind.Ntfs:
                DiagnoseNtfs(partition, report, ct);
                break;
            case FileSystemKind.Fat32:
                DiagnoseFat32(partition, report, ct);
                break;
            case FileSystemKind.ExFat:
                report.Severity = DiagnosisSeverity.Info;
                report.Title = "exFAT – zatiaľ nepodporované";
                report.Details = "Táto verzia vie čítať a opravovať NTFS a FAT32. " +
                                 "exFAT je rozpoznaný, ale záchrana preň zatiaľ nie je implementovaná.";
                break;
            default:
                DiagnoseRaw(report);
                break;
        }

        return report;
    }

    private static void DiagnoseNtfs(DiskPartition partition, QuickDiagnosisReport report,
        CancellationToken ct)
    {
        try
        {
            using var disk = partition.OpenDisk();
            var volume = NtfsVolume.Open(disk, partition.Offset, partition.Length);
            report.NtfsReadable = true;
            report.BootFromBackup = volume.BootFromBackup;

            var root = volume.ReadRecordCached(5);
            bool rootOk = root is { IsDirectory: true };

            string details = $"Boot sector: {(volume.PrimaryBoot?.IsValid == true ? "OK" : "poškodený")}\n" +
                             $"$MFT: {volume.TotalRecords:N0} záznamov\n" +
                             $"Koreňový adresár: {(rootOk ? "OK" : "poškodený")}";

            if (partition.IsImage)
            {
                report.Severity = DiagnosisSeverity.Ok;
                report.Title = "Obraz: NTFS je čitateľný";
                report.Details = details + "\n\nObraz je len na čítanie. Môžete rovno záchranu súborov alebo hĺbkový sken.";
                report.Actions.Add(RecommendedAction.CopyFiles);
                report.Actions.Add(RecommendedAction.DeepScan);
                return;
            }

            VolumeEntry? fresh = null;
            bool? dirty = null;
            if (partition.HasLetter)
            {
                fresh = VolumeCatalog.GetOneAsync(partition.Letter, ct).GetAwaiter().GetResult();
                dirty = DiagnosticsService.QueryDirtyAsync(partition.Letter, null, ct).GetAwaiter().GetResult();
                if (fresh is not null)
                    details += $"\nWindows: {fresh.Health} / {fresh.Status}" +
                               (string.IsNullOrWhiteSpace(fresh.FileSystem) ? " / nenamontovaný" : "");
                if (dirty.HasValue)
                    details += $"\nDirty bit: {(dirty.Value ? "Dirty" : "čistý")}";
            }

            if (report.BootFromBackup)
            {
                report.Severity = DiagnosisSeverity.Warning;
                report.Title = "NTFS boot sector je poškodený, záloha je v poriadku";
                report.Details = details + "\n\nNajprv obnovte boot sector zo zálohy.";
                report.Actions.Add(RecommendedAction.RestoreBoot);
                report.Actions.Add(RecommendedAction.CopyFiles);
                report.Actions.Add(RecommendedAction.Repair);
                return;
            }

            if (!partition.HasLetter)
            {
                report.Severity = DiagnosisSeverity.Warning;
                report.Title = "NTFS je čitateľné, ale chýba mu písmeno";
                report.Details = details + "\n\nOprava sa pokúsi priradiť dočasné písmeno.";
                report.Actions.Add(RecommendedAction.CopyFiles);
                report.Actions.Add(RecommendedAction.Repair);
                report.Actions.Add(RecommendedAction.DeepScan);
                return;
            }

            bool mounted = fresh is not null &&
                           !string.IsNullOrWhiteSpace(fresh.FileSystem) &&
                           fresh.Size > 0;
            bool accessible = Directory.Exists($"{partition.DriveLetter}:\\");

            if (!mounted || !accessible)
            {
                report.Severity = DiagnosisSeverity.Warning;
                report.Title = mounted
                    ? "NTFS je namontované, ale prístup je zamietnutý"
                    : "NTFS je čitateľné, ale Windows volume nenamontoval";
                report.Details = details +
                    "\n\nchkdsk môže byť čistý, ale Windows stále hlási Access Denied. " +
                    "Oprava automaticky skúsi volume odpojiť a znova pripojiť; ak treba, obnoví ACL na koreni.";
                report.Actions.Add(RecommendedAction.CopyFiles);
                report.Actions.Add(RecommendedAction.Repair);
                return;
            }

            if (!VolumeAccessFixer.HasUserAccess(partition.Letter))
            {
                report.Severity = DiagnosisSeverity.Warning;
                report.Title = "NTFS je namontované, ale ACL blokuje bežného používateľa";
                report.Details = details +
                    "\n\nKoreň disku má oprávnenia len pre SYSTEM a Administrators. " +
                    "Oprava ich automaticky obnoví (takeown + icacls).";
                report.Actions.Add(RecommendedAction.CopyFiles);
                report.Actions.Add(RecommendedAction.Repair);
                return;
            }

            if (dirty == true || fresh?.IsProblem == true)
            {
                report.Severity = DiagnosisSeverity.Warning;
                report.Title = "NTFS je čitateľné, ale Windows ho blokuje";
                report.Details = details + "\n\nŠtruktúra sa dá prečítať, takže najprv skopírujte dôležité súbory a potom skúste opravu.";
                report.Actions.Add(RecommendedAction.CopyFiles);
                report.Actions.Add(RecommendedAction.Repair);
                report.Actions.Add(RecommendedAction.DeepScan);
                return;
            }

            report.Severity = DiagnosisSeverity.Ok;
            report.Title = "NTFS vyzerá čitateľne";
            report.Details = details + "\n\nAk Windows napriek tomu nevidí obsah, skúste opravu.";
            report.Actions.Add(RecommendedAction.CopyFiles);
            report.Actions.Add(RecommendedAction.Repair);
            report.Actions.Add(RecommendedAction.DeepScan);
        }
        catch (Exception ex)
        {
            report.NtfsReadable = false;
            report.Severity = DiagnosisSeverity.Critical;
            report.Title = "NTFS štruktúru sa nepodarilo prečítať";
            report.Details = "Windows volume je zablokovaný alebo je poškodené $MFT.\n\n" + ex.Message;
            report.Actions.Add(RecommendedAction.CopyFiles);
            report.Actions.Add(RecommendedAction.DeepScan);
            report.Actions.Add(RecommendedAction.Repair);
        }
    }

    private static void DiagnoseFat32(DiskPartition partition, QuickDiagnosisReport report,
        CancellationToken ct)
    {
        try
        {
            var diag = Fat32RepairService.Diagnose(partition, ct);
            report.FatBootValid = diag.BootValid;
            report.FatBackupValid = diag.BackupValid;
            report.Fat1Valid = diag.Fat1Valid;
            report.Fat2Valid = diag.Fat2Valid;

            string details = $"Boot sector: {(diag.BootValid ? "OK" : diag.PrimaryError)}\n" +
                             $"Záložný boot: {(diag.BackupValid ? "OK" : diag.BackupError)}\n" +
                             $"FAT1: {(diag.Fat1Valid ? "OK" : "poškodená")}\n" +
                             $"FAT2: {(diag.Fat2Valid ? "OK" : "poškodená")}\n" +
                             $"Root: {(diag.RootReadable ? "čitateľný" : "nečitateľný")}";

            if (partition.IsImage)
            {
                report.Severity = diag.RootReadable ? DiagnosisSeverity.Ok : DiagnosisSeverity.Warning;
                report.Title = "Obraz: FAT32 je čitateľná";
                report.Details = details + "\n\nObraz je len na čítanie. Môžete rovno záchranu súborov.";
                report.Actions.Add(RecommendedAction.CopyFiles);
                return;
            }

            if (!diag.BootValid && diag.BackupValid)
            {
                report.Severity = DiagnosisSeverity.Warning;
                report.Title = "FAT32 boot sector je poškodený, záloha je v poriadku";
                report.Details = details + "\n\nOdporúčam obnoviť boot sector zo zálohy (sektor 6).";
                report.Actions.Add(RecommendedAction.RestoreBoot);
                report.Actions.Add(RecommendedAction.CopyFiles);
                report.Actions.Add(RecommendedAction.Repair);
                return;
            }

            if (diag.Fat1Valid == false && diag.Fat2Valid)
            {
                report.Severity = DiagnosisSeverity.Warning;
                report.Title = "FAT1 je poškodená, FAT2 vyzerá použiteľne";
                report.Details = details + "\n\nFAT1 sa dá obnoviť z FAT2. Predtým sa uloží záloha.";
                report.Actions.Add(RecommendedAction.RestoreFat);
                report.Actions.Add(RecommendedAction.CopyFiles);
                report.Actions.Add(RecommendedAction.Repair);
                return;
            }

            if (!diag.BootValid && !diag.BackupValid)
            {
                report.Severity = DiagnosisSeverity.Critical;
                report.Title = "FAT32 boot sector je poškodený";
                report.Details = details + "\n\nBez boot sectoru treba hĺbkovú záchranu alebo obnovu zálohy.";
                report.Actions.Add(RecommendedAction.CopyFiles);
                report.Actions.Add(RecommendedAction.Repair);
                return;
            }

            if (!partition.HasLetter)
            {
                report.Severity = DiagnosisSeverity.Warning;
                report.Title = "FAT32 je čitateľná, ale chýba jej písmeno";
                report.Details = details + "\n\nProgram môže priradiť dočasné písmeno a obnoviť prístup.";

            }

            var fresh = VolumeCatalog.GetOneAsync(partition.Letter, ct).GetAwaiter().GetResult();
            bool mounted = fresh is not null &&
                           !string.IsNullOrWhiteSpace(fresh.FileSystem) &&
                           fresh.Size > 0;
            bool accessible = Directory.Exists($"{partition.DriveLetter}:\\");
            if (!mounted || !accessible)
            {
                report.Severity = DiagnosisSeverity.Warning;
                report.Title = mounted
                    ? "FAT32 je namontovaná, ale prístup je zamietnutý"
                    : "FAT32 je čitateľná, ale Windows volume nenamontoval";
                report.Details = details + "\n\nWindows hlási Access Denied. Program skúsi volume odpojiť a znova pripojiť.";

            }

            report.Severity = diag.RootReadable ? DiagnosisSeverity.Ok : DiagnosisSeverity.Warning;
            report.Title = diag.RootReadable ? "FAT32 vyzerá čitateľne" : "FAT32 má problém s root adresárom";
            report.Details = details;
            report.Actions.Add(RecommendedAction.CopyFiles);
            report.Actions.Add(RecommendedAction.Repair);
        }
        catch (Exception ex)
        {
            report.Severity = DiagnosisSeverity.Critical;
            report.Title = "FAT32 sa nedá prečítať";
            report.Details = ex.Message;
            report.Actions.Add(RecommendedAction.CopyFiles);
            report.Actions.Add(RecommendedAction.Repair);
        }
    }

    private static void DiagnoseRaw(QuickDiagnosisReport report)
    {
        report.Severity = DiagnosisSeverity.Warning;
        report.Title = "Systém sa tvári ako RAW";
        report.Details = "Nepodarilo sa rozpoznať platný NTFS ani FAT32 boot sector.\n\n" +
                         "Ak viete, aký systém tam bol, vyberte ho. Pri „Neviem“ program vyskúša oba parsery len na čítanie.";
        report.Actions.Add(RecommendedAction.TryNtfs);
        report.Actions.Add(RecommendedAction.TryFat32);
        report.Actions.Add(RecommendedAction.DeepScan);
    }
}
