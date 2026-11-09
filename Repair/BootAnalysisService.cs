using System.Diagnostics;
using NtfsRescue.Core;
using NtfsRescue.Util;

namespace NtfsRescue.Repair;

public enum FirmwareMode { Unknown, Uefi, Bios }
public enum BootMode { Unknown, Uefi, Bios }
public enum BootCheckLevel { Ok, Info, Warning, Error }

public sealed class BootCheck
{
    public string Name = "";
    public string Detail = "";
    public BootCheckLevel Level = BootCheckLevel.Info;
    public bool Blocking;
}

public sealed class BootRecoveryImage
{
    public DiskPartition Partition = null!;
    public string Path = "";
    public string RelativePath = "";
    public bool IsWindowsRe;
    public bool IsOem;
    public string Kind = "";
}

public sealed class BootAnalysis
{
    public DiskPartition Target = null!;
    public List<DiskPartition> Partitions = new();
    public FirmwareMode Firmware = FirmwareMode.Unknown;
    public BootMode BootMode = BootMode.Unknown;
    public string Vendor = "";
    public string Manufacturer = "";
    public string Model = "";
    public bool IsUefi => BootMode == BootMode.Uefi;
    public bool IsBios => BootMode == BootMode.Bios;

    public List<DiskPartition> WindowsPartitions = new();
    public string WindowsVersion = "";
    public DiskPartition? Esp;
    public DiskPartition? ActivePartition;
    public DiskPartition? SystemPartition;
    public DiskPartition? RecoveryPartition;
    public List<DiskPartition> OemRecoveryPartitions = new();
    public List<BootRecoveryImage> RecoveryImages = new();
    public BootRecoveryImage? WinReImage;
    public BootRecoveryImage? OemImage;

    public string EspPath = "";
    public string SystemPath = "";
    public string BcdPath = "";
    public string WindowsPath = "";
    public string BcdOutput = "";
    public bool BcdExists;
    public bool BcdValid;
    public bool BcdHasWindowsLoader;
    public bool BcdHasBootManager;
    public bool HasBootManagerFile;
    public bool HasWinload;
    public bool HasFallbackEfi;
    public bool WinRePresent;
    public bool WinReRegistered;
    public string ReagentStatus = "";

    public bool MbrSignatureValid;
    public bool MbrBootCodePresent;
    public int MbrActiveCount;
    public bool VbrValid;
    public bool SignatureCollision;

    public readonly List<BootCheck> Checks = new();
    public readonly List<string> Problems = new();
    public string Summary = "";
    public bool LayoutCompatible;
    public bool FirmwareMismatch;
    public bool CanRepair;
    public bool CanConvert;
    public bool CanAddWinRe;
    public bool CanAddOemRecovery;
    public string RepairBlockedReason = "";
    public string ConversionBlockedReason = "";
}

public static class BootAnalysisService
{
    private static string SysExe(string name) => Path.Combine(Environment.SystemDirectory, name);

    public static async Task<BootAnalysis> AnalyzeDiskAsync(
        DiskPartition sample,
        IReadOnlyList<DiskPartition>? allPartitions,
        Action<string>? log,
        IProgress<string>? phase,
        CancellationToken ct)
    {
        var all = allPartitions?.ToList() ?? await DiskCatalog.LoadAllAsync(ct).ConfigureAwait(false);
        var target = all.Where(p => p.DiskNumber == sample.DiskNumber).ToList();
        var analysis = new BootAnalysis
        {
            Target = target.FirstOrDefault() ?? sample,
            Partitions = target
        };

        var mounted = new Dictionary<DiskPartition, bool>();
        try
        {
            phase?.Report("Zisťujem firmware a disk");
            analysis.Firmware = await GetFirmwareAsync(ct).ConfigureAwait(false);
            (analysis.Manufacturer, analysis.Model, analysis.Vendor) =
                await GetVendorAsync(ct).ConfigureAwait(false);

            analysis.BootMode = analysis.Target.IsGpt ? BootMode.Uefi : BootMode.Bios;
            analysis.LayoutCompatible = analysis.Target.IsGpt
                ? target.Any(p => p.IsEsp)
                : target.Any(p => p.IsActive) || target.Any(p => p.FileSystem == FileSystemKind.Ntfs);
            analysis.FirmwareMismatch = IsFirmwareMismatch(analysis.Target.IsGpt, analysis.Firmware);

            phase?.Report("Montujem partície");
            foreach (var partition in target)
            {
                ct.ThrowIfCancellationRequested();
                if (partition.Length <= 0 || partition.IsOffline) continue;
                if (partition.FileSystem is not (FileSystemKind.Ntfs or FileSystemKind.Fat32 or FileSystemKind.Unknown))
                    continue;

                var hadLetter = partition.HasLetter;
                var letter = await BootVolumeAccess.EnsureLetterAsync(partition, log, ct).ConfigureAwait(false);
                if (letter.HasValue && !hadLetter)
                    mounted[partition] = true;
                else if (!letter.HasValue)
                    log?.Invoke($"Partíciu {partition.PartitionNumber} sa nepodarilo namontovať.");
            }

            FindWindows(analysis, log);
            FindSystemPartitions(analysis);
            CheckBootFiles(analysis);
            await CheckBcdAsync(analysis, log, ct).ConfigureAwait(false);
            CheckMbr(analysis, all, log);
            FindRecovery(analysis, log);
            await CheckRecoveryRegistrationAsync(analysis, log, ct).ConfigureAwait(false);

            BuildChecks(analysis);
            BuildSummary(analysis);
            return analysis;
        }
        finally
        {
            foreach (var (partition, _) in mounted)
                await BootVolumeAccess.ReleaseAsync(partition, true, CancellationToken.None)
                    .ConfigureAwait(false);
        }
    }

    private static async Task<FirmwareMode> GetFirmwareAsync(CancellationToken ct)
    {
        try
        {
            var doc = await Ps.RunJsonAsync(
                "Get-ComputerInfo -Property BiosFirmwareType | Select-Object BiosFirmwareType | ConvertTo-Json -Compress",
                ct, 30000).ConfigureAwait(false);
            var value = doc is null ? "" : Ps.Str(Ps.Objects(doc).FirstOrDefault(), "BiosFirmwareType");
            if (value.Contains("Uefi", StringComparison.OrdinalIgnoreCase)) return FirmwareMode.Uefi;
            if (value.Contains("Bios", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("Legacy", StringComparison.OrdinalIgnoreCase)) return FirmwareMode.Bios;
        }
        catch { }

        try
        {
            var env = await Ps.RunAsync("$env:firmware_type", ct, 10000).ConfigureAwait(false);
            if (env?.Contains("UEFI", StringComparison.OrdinalIgnoreCase) == true) return FirmwareMode.Uefi;
            if (env?.Contains("BIOS", StringComparison.OrdinalIgnoreCase) == true) return FirmwareMode.Bios;
        }
        catch { }

        return FirmwareMode.Unknown;
    }

    private static async Task<(string Manufacturer, string Model, string Vendor)> GetVendorAsync(
        CancellationToken ct)
    {
        try
        {
            var doc = await Ps.RunJsonAsync(
                "Get-CimInstance Win32_ComputerSystem | Select-Object Manufacturer,Model | ConvertTo-Json -Compress",
                ct, 20000).ConfigureAwait(false);
            var obj = doc is null ? default : Ps.Objects(doc).FirstOrDefault();
            var manufacturer = Ps.Str(obj, "Manufacturer");
            var model = Ps.Str(obj, "Model");
            var vendor = manufacturer;
            foreach (var name in new[] { "HP", "Hewlett", "LENOVO", "Lenovo", "DELL", "Dell",
                         "ASUS", "ASUSTeK", "Acer", "Samsung", "MSI", "Toshiba", "Fujitsu" })
            {
                if (manufacturer.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                    model.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    vendor = name;
                    break;
                }
            }
            return (manufacturer, model, vendor);
        }
        catch
        {
            return ("", "", "");
        }
    }

    private static void FindWindows(BootAnalysis analysis, Action<string>? log)
    {
        foreach (var partition in analysis.Partitions)
        {
            if (!partition.HasLetter) continue;
            try
            {
                var root = $"{partition.DriveLetter}:\\";
                var win = Path.Combine(root, "Windows");
                var winload = Path.Combine(win, "System32", "winload.exe");
                var winloadEfi = Path.Combine(win, "System32", "winload.efi");
                var kernel = Path.Combine(win, "System32", "ntoskrnl.exe");
                if (!Directory.Exists(win) || (!File.Exists(winload) && !File.Exists(winloadEfi)))
                    continue;

                analysis.WindowsPartitions.Add(partition);
                if (string.IsNullOrWhiteSpace(analysis.WindowsPath))
                {
                    analysis.WindowsPath = win;
                    analysis.HasWinload = File.Exists(winloadEfi) || File.Exists(winload);
                    try
                    {
                        var info = FileVersionInfo.GetVersionInfo(kernel);
                        if (!string.IsNullOrWhiteSpace(info.FileVersion))
                            analysis.WindowsVersion = info.FileVersion;
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"Kontrola Windows na {partition.DriveLetter}: zlyhala: {ex.Message}");
            }
        }
    }

    private static void FindSystemPartitions(BootAnalysis analysis)
    {
        var target = analysis.Partitions;

        if (analysis.Target.IsGpt)
        {
            analysis.Esp = target.FirstOrDefault(p => p.IsEsp)
                           ?? target.FirstOrDefault(p => p.HasLetter &&
                               Directory.Exists($"{p.DriveLetter}:\\EFI\\Microsoft\\Boot"));
        }
        else
        {
            analysis.ActivePartition = target.FirstOrDefault(p => p.IsActive);
            analysis.SystemPartition = analysis.ActivePartition
                ?? target.FirstOrDefault(p => p.HasLetter && File.Exists($"{p.DriveLetter}:\\bootmgr"))
                ?? analysis.WindowsPartitions.FirstOrDefault();
            analysis.SystemPath = analysis.SystemPartition?.HasLetter == true
                ? $"{analysis.SystemPartition.DriveLetter}:\\"
                : "";
        }

        if (analysis.Esp is not null && analysis.Esp.HasLetter)
            analysis.EspPath = $"{analysis.Esp.DriveLetter}:\\";

        analysis.SystemPath = analysis.Target.IsGpt
            ? analysis.EspPath
            : analysis.SystemPath;

        analysis.BcdPath = analysis.Target.IsGpt
            ? Path.Combine(analysis.EspPath, "EFI", "Microsoft", "Boot", "BCD")
            : Path.Combine(analysis.SystemPath, "Boot", "BCD");

        if (!analysis.Target.IsGpt && !File.Exists(analysis.BcdPath) &&
            analysis.WindowsPartitions.FirstOrDefault() is { HasLetter: true } win)
            analysis.BcdPath = Path.Combine($"{win.DriveLetter}:\\", "Boot", "BCD");
    }

    private static void CheckBootFiles(BootAnalysis analysis)
    {
        if (analysis.IsUefi)
        {
            if (analysis.Esp is null) return;
            analysis.HasBootManagerFile = File.Exists(
                Path.Combine(analysis.EspPath, "EFI", "Microsoft", "Boot", "bootmgfw.efi"));
            analysis.HasFallbackEfi = File.Exists(
                Path.Combine(analysis.EspPath, "EFI", "Boot", "bootx64.efi"));
            analysis.BcdExists = File.Exists(analysis.BcdPath);
            if (analysis.Esp.FileSystem != FileSystemKind.Fat32)
                analysis.Problems.Add("ESP nie je FAT32.");
        }
        else
        {
            if (analysis.SystemPartition is null) return;
            analysis.HasBootManagerFile = File.Exists(
                Path.Combine(analysis.SystemPath, "bootmgr"));
            analysis.BcdExists = File.Exists(analysis.BcdPath);
            analysis.HasFallbackEfi = false;
        }
    }

    private static async Task CheckBcdAsync(BootAnalysis analysis, Action<string>? log,
        CancellationToken ct)
    {
        if (!analysis.BcdExists)
        {
            analysis.BcdValid = false;
            return;
        }

        var bcdedit = SysExe("bcdedit.exe");
        if (!File.Exists(bcdedit))
        {
            log?.Invoke("bcdedit.exe sa nenašiel.");
            return;
        }

        try
        {
            var result = await ProcessRunner.RunAsync(bcdedit,
                new[] { "/store", analysis.BcdPath, "/enum", "all" },
                null, ct, 30000).ConfigureAwait(false);
            analysis.BcdOutput = result.Output;
            var text = result.Output;
            analysis.BcdHasWindowsLoader =
                text.Contains("winload.efi", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("winload.exe", StringComparison.OrdinalIgnoreCase);
            analysis.BcdHasBootManager =
                text.Contains("bootmgfw.efi", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("bootmgr", StringComparison.OrdinalIgnoreCase);
            analysis.BcdValid = result.ExitCode == 0 && analysis.BcdHasWindowsLoader;
        }
        catch (Exception ex)
        {
            log?.Invoke("BCD sa nepodarilo prečítať: " + ex.Message);
        }
    }

    private static void CheckMbr(BootAnalysis analysis, IReadOnlyList<DiskPartition> all,
        Action<string>? log)
    {
        analysis.MbrSignatureValid = false;
        analysis.MbrBootCodePresent = false;
        analysis.MbrActiveCount = 0;
        analysis.VbrValid = false;

        try
        {
            using var disk = new RawDisk(analysis.Target.DiskNumber, analysis.Target.SectorSize,
                analysis.Target.DiskSize);
            var mbr = disk.ReadBytes(0, 512);
            analysis.MbrSignatureValid = IsMbrValid(mbr);
            analysis.MbrBootCodePresent = HasMbrBootCode(mbr);
            analysis.MbrActiveCount = CountActivePartitions(mbr);

            if (!analysis.Target.IsGpt)
            {
                var active = analysis.Partitions.FirstOrDefault(p => p.IsActive);
                if (active is not null)
                {
                    try
                    {
                        var vbr = disk.ReadBytes(active.Offset, 512);
                        analysis.VbrValid = IsVbrValid(vbr);
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke("MBR sa nepodarilo prečítať: " + ex.Message);
        }

        if (!analysis.Target.IsGpt)
        {
            var signatures = all
                .GroupBy(p => p.DiskNumber)
                .Select(g => g.First())
                .Where(p => !string.IsNullOrWhiteSpace(p.DiskSignature))
                .GroupBy(p => p.DiskSignature)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet();
            analysis.SignatureCollision = signatures.Contains(analysis.Target.DiskSignature);
        }
    }

    private static void FindRecovery(BootAnalysis analysis, Action<string>? log)
    {
        var images = new List<BootRecoveryImage>();
        foreach (var partition in analysis.Partitions)
        {
            if (!partition.HasLetter) continue;
            var root = $"{partition.DriveLetter}:\\";
            TryAddWim(images, partition, Path.Combine(root, "Recovery", "WindowsRE", "WinRE.wim"), true);
            TryAddWim(images, partition, Path.Combine(root, "Windows", "System32", "Recovery", "WinRE.wim"), true);
            TryAddWim(images, partition, Path.Combine(root, "sources", "boot.wim"), false);
            TryAddWim(images, partition, Path.Combine(root, "boot.wim"), false);

            try
            {
                var recoveryRoot = Path.Combine(root, "Recovery");
                if (Directory.Exists(recoveryRoot))
                {
                    foreach (var wim in Directory.EnumerateFiles(recoveryRoot, "*.wim",
                                 SearchOption.AllDirectories).Take(100))
                    {
                        var name = Path.GetFileName(wim);
                        if (!name.Equals("boot.wim", StringComparison.OrdinalIgnoreCase) &&
                            !name.Equals("winre.wim", StringComparison.OrdinalIgnoreCase))
                            continue;
                        TryAddWim(images, partition, wim,
                            name.Equals("winre.wim", StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
            catch { }
        }

        analysis.RecoveryImages = images;
        analysis.WinReImage = images.FirstOrDefault(i => i.IsWindowsRe);
        analysis.OemImage = images.FirstOrDefault(i => !i.IsWindowsRe);
        analysis.WinRePresent = analysis.WinReImage is not null;

        analysis.OemRecoveryPartitions = analysis.Partitions
            .Where(p => p.IsRecovery ||
                        p.Label.Contains("RECOVERY", StringComparison.OrdinalIgnoreCase) ||
                        p.Label.Contains("OEM", StringComparison.OrdinalIgnoreCase) ||
                        p.PartitionType.Contains("Recovery", StringComparison.OrdinalIgnoreCase))
            .ToList();

        analysis.RecoveryPartition = analysis.OemRecoveryPartitions.FirstOrDefault()
                                     ?? analysis.RecoveryImages.FirstOrDefault()?.Partition;
        if (analysis.WinReImage is null && analysis.OemRecoveryPartitions.Count > 0)
            log?.Invoke("Našla sa recovery partícia, ale neobsahuje WinRE.wim ani boot.wim.");
    }

    private static void TryAddWim(List<BootRecoveryImage> images, DiskPartition partition,
        string path, bool isWinRe)
    {
        try
        {
            if (!File.Exists(path)) return;
            if (images.Any(i => i.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) return;
            var root = $"{partition.DriveLetter}:\\";
            images.Add(new BootRecoveryImage
            {
                Partition = partition,
                Path = path,
                RelativePath = path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    ? path[root.Length..]
                    : Path.GetFileName(path),
                IsWindowsRe = isWinRe,
                IsOem = !isWinRe,
                Kind = isWinRe ? "Windows RE" : "OEM / WinPE"
            });
        }
        catch { }
    }

    private static async Task CheckRecoveryRegistrationAsync(BootAnalysis analysis,
        Action<string>? log, CancellationToken ct)
    {
        var text = analysis.BcdOutput.ToLowerInvariant();
        analysis.WinReRegistered = text.Contains("winre.wim") || text.Contains("winre");
        if (!analysis.Target.DiskIsSystem) return;

        try
        {
            var reagent = SysExe("reagentc.exe");
            if (!File.Exists(reagent)) return;
            var result = await ProcessRunner.RunAsync(reagent, new[] { "/info" }, null, ct, 20000)
                .ConfigureAwait(false);
            analysis.ReagentStatus = result.Output;
            analysis.WinReRegistered = result.Output.Contains("Enabled", StringComparison.OrdinalIgnoreCase) ||
                                       result.Output.Contains("Zapnut", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            log?.Invoke("reagentc sa nepodarilo spustiť: " + ex.Message);
        }
    }

    internal static bool IsFirmwareMismatch(bool isGpt, FirmwareMode firmware)
        => (firmware == FirmwareMode.Uefi && !isGpt) ||
           (firmware == FirmwareMode.Bios && isGpt);

    internal static bool IsMbrValid(byte[] mbr)
        => mbr.Length >= 512 && mbr[510] == 0x55 && mbr[511] == 0xAA;

    internal static bool HasMbrBootCode(byte[] mbr)
        => mbr.Length >= 440 && mbr.Take(440).Any(b => b != 0);

    internal static bool IsVbrValid(byte[] vbr)
        => vbr.Length >= 512 && vbr[510] == 0x55 && vbr[511] == 0xAA;

    internal static int CountActivePartitions(byte[] mbr)
    {
        if (mbr.Length < 512) return 0;
        var count = 0;
        for (int i = 0; i < 4; i++)
            if (mbr[0x1BE + i * 16] == 0x80) count++;
        return count;
    }

    private static void BuildChecks(BootAnalysis analysis)
    {
        void Add(string name, string detail, BootCheckLevel level, bool blocking = false)
            => analysis.Checks.Add(new BootCheck { Name = name, Detail = detail, Level = level, Blocking = blocking });

        Add("Firmware", analysis.Firmware switch
        {
            FirmwareMode.Uefi => "UEFI",
            FirmwareMode.Bios => "BIOS / Legacy",
            _ => "nezistený"
        }, BootCheckLevel.Info);

        Add("Štýl disku", analysis.Target.IsGpt ? "GPT" : "MBR", BootCheckLevel.Info);

        if (analysis.FirmwareMismatch)
        {
            Add("Firmware a disk", $"Disk je {analysis.Target.PartitionStyle}, počítač je {analysis.Firmware}. " +
                "Tento disk nemusí na tomto počítači štartovať bez zmeny BIOS/UEFI alebo konverzie.",
                BootCheckLevel.Warning);
        }

        if (analysis.WindowsPartitions.Count == 0)
            Add("Windows", "Na disku sa nenašla Windows inštalácia.", BootCheckLevel.Error, true);
        else
            Add("Windows", $"{analysis.WindowsPartitions.Count} inštalácia: " +
                string.Join(", ", analysis.WindowsPartitions.Select(p =>
                    p.HasLetter ? $"{p.DriveLetter}:" : $"partícia {p.PartitionNumber}")) +
                (string.IsNullOrWhiteSpace(analysis.WindowsVersion) ? "" : $" ({analysis.WindowsVersion})"),
                BootCheckLevel.Ok);

        if (analysis.IsUefi)
        {
            if (analysis.Esp is null)
                Add("ESP", "Chýba EFI System Partition. UEFI boot sa nedá obnoviť bez jej vytvorenia.", BootCheckLevel.Error, true);
            else
                Add("ESP", $"{(analysis.Esp.HasLetter ? analysis.Esp.DriveLetter + ":" : "bez písmena")} · " +
                    $"{analysis.Esp.FileSystem.ToSlovak()} · {analysis.Esp.Length / 1024 / 1024} MB",
                    analysis.Esp.FileSystem == FileSystemKind.Fat32 ? BootCheckLevel.Ok : BootCheckLevel.Warning);

            Add("bootmgfw.efi", analysis.HasBootManagerFile ? "OK" : "chýba",
                analysis.HasBootManagerFile ? BootCheckLevel.Ok : BootCheckLevel.Error, !analysis.HasBootManagerFile);
            Add("Fallback \\EFI\\Boot\\bootx64.efi", analysis.HasFallbackEfi ? "OK" : "chýba (vhodné doplniť pri klonoch)",
                analysis.HasFallbackEfi ? BootCheckLevel.Ok : BootCheckLevel.Warning);
        }
        else
        {
            if (analysis.ActivePartition is null)
                Add("Active partícia", "Chýba active flag. Oprava ho môže nastaviť.", BootCheckLevel.Warning);
            else
                Add("Active partícia", $"partícia {analysis.ActivePartition.PartitionNumber}" +
                    (analysis.ActivePartition.HasLetter ? $" ({analysis.ActivePartition.DriveLetter}:)" : ""),
                    BootCheckLevel.Ok);

            Add("bootmgr", analysis.HasBootManagerFile ? "OK" : "chýba",
                analysis.HasBootManagerFile ? BootCheckLevel.Ok : BootCheckLevel.Error, !analysis.HasBootManagerFile);
            Add("MBR boot code", analysis.MbrBootCodePresent ? "prítomný" : "chýba / nulový",
                analysis.MbrBootCodePresent ? BootCheckLevel.Ok : BootCheckLevel.Warning);
            Add("MBR podpis", analysis.MbrSignatureValid ? "55AA OK" : "neplatný",
                analysis.MbrSignatureValid ? BootCheckLevel.Ok : BootCheckLevel.Error);
            if (analysis.ActivePartition is not null)
                Add("VBR systémovej partície", analysis.VbrValid ? "OK" : "podozrivý",
                    analysis.VbrValid ? BootCheckLevel.Ok : BootCheckLevel.Warning);
            if (analysis.SignatureCollision)
                Add("Disk signature", "Kolízia s iným diskom — pri klone môže Windows vybrať nesprávny disk.",
                    BootCheckLevel.Warning);
        }

        Add("BCD", !analysis.BcdExists ? "chýba" : analysis.BcdValid ? "OK" : "poškodené alebo bez Windows loadera",
            analysis.BcdValid ? BootCheckLevel.Ok : BootCheckLevel.Error, !analysis.BcdValid);
        Add("Windows loader v BCD", analysis.BcdHasWindowsLoader ? "nájdený" : "chýba",
            analysis.BcdHasWindowsLoader ? BootCheckLevel.Ok : BootCheckLevel.Error, !analysis.BcdHasWindowsLoader);

        if (analysis.WinRePresent)
            Add("Windows RE", $"{analysis.WinReImage!.Kind}: {analysis.WinReImage.Path}" +
                (analysis.WinReRegistered ? " · zaregistrované" : " · nezaregistrované"),
                analysis.WinReRegistered ? BootCheckLevel.Ok : BootCheckLevel.Info);
        else if (analysis.OemRecoveryPartitions.Count > 0)
            Add("Recovery", "Nájdená recovery partícia, ale bez WinRE.wim/boot.wim.",
                BootCheckLevel.Warning);
        else
            Add("Recovery", "Windows RE ani OEM recovery sa nenašli.", BootCheckLevel.Info);

        if (analysis.OemImage is not null)
            Add("OEM recovery", $"{analysis.Vendor} · {analysis.OemImage.Path}",
                BootCheckLevel.Info);
        else if (analysis.OemRecoveryPartitions.Count > 0)
            Add("OEM recovery", $"{analysis.Vendor} · partícia {analysis.OemRecoveryPartitions[0].PartitionNumber}",
                BootCheckLevel.Info);

        foreach (var problem in analysis.Problems)
            Add("Pozor", problem, BootCheckLevel.Warning);
    }

    private static void BuildSummary(BootAnalysis analysis)
    {
        analysis.RepairBlockedReason = "";
        if (analysis.Target.DiskIsSystem)
            analysis.RepairBlockedReason = "Toto je aktuálny systémový disk. Oprava je zablokovaná; spustite ju z iného systému alebo WinRE.";
        else if (analysis.Target.DiskIsOffline)
            analysis.RepairBlockedReason = "Disk je offline.";
        else if (analysis.Target.DiskIsReadOnly)
            analysis.RepairBlockedReason = "Disk je read-only.";
        else if (analysis.WindowsPartitions.Count == 0)
            analysis.RepairBlockedReason = "Nenašla sa Windows inštalácia.";
        else if (analysis.IsUefi && analysis.Esp is null)
            analysis.RepairBlockedReason = "Chýba ESP; automatická oprava nie je možná.";
        else if (!File.Exists(SysExe("bcdboot.exe")))
            analysis.RepairBlockedReason = "Chýba bcdboot.exe.";
        else if (analysis.IsBios && !File.Exists(SysExe("bootsect.exe")))
            analysis.RepairBlockedReason = "Chýba bootsect.exe.";

        analysis.CanRepair = string.IsNullOrEmpty(analysis.RepairBlockedReason);
        analysis.ConversionBlockedReason = "";
        if (analysis.Target.DiskIsSystem) analysis.ConversionBlockedReason = analysis.RepairBlockedReason;
        else if (analysis.Target.IsGpt) analysis.ConversionBlockedReason = "Disk je už GPT; konverzia MBR→GPT sa nedá použiť.";
        else if (analysis.WindowsPartitions.Count == 0) analysis.ConversionBlockedReason = "Nenašla sa Windows inštalácia.";
        else if (!File.Exists(SysExe("mbr2gpt.exe"))) analysis.ConversionBlockedReason = "Chýba mbr2gpt.exe.";
        analysis.CanConvert = string.IsNullOrEmpty(analysis.ConversionBlockedReason);
        analysis.CanAddWinRe = !analysis.Target.DiskIsSystem &&
                               analysis.WinReImage is not null &&
                               !analysis.WinReRegistered;
        analysis.CanAddOemRecovery = !analysis.Target.DiskIsSystem && analysis.OemImage is not null;

        analysis.Summary = analysis.WindowsPartitions.Count == 0
            ? "Na disku sa nenašla Windows inštalácia."
            : !analysis.BcdValid || !analysis.HasBootManagerFile
                ? "Bootovanie je poškodené alebo neúplné. Odporúčaná oprava boot súborov a BCD."
                : "Boot súbory vyzerajú v poriadku. Skontrolujte BIOS/UEFI nastavenia a poradie bootovania.";
    }
}
