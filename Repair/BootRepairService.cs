using System.Diagnostics;
using System.Text;
using NtfsRescue.Core;
using NtfsRescue.Util;

namespace NtfsRescue.Repair;

public sealed class BootRepairResult
{
    public bool Success;
    public string Summary = "";
    public string BackupFolder = "";
    public readonly List<string> Details = new();
}

public sealed class BootRecoveryResult
{
    public bool Success;
    public string Summary = "";
    public string BackupFolder = "";
    public string EntryGuid = "";
}

public static class BootRepairService
{
    private static string SysExe(string name) => Path.Combine(Environment.SystemDirectory, name);

    public static async Task<BootRepairResult> RepairAsync(
        BootAnalysis analysis,
        bool renewDiskSignature,
        Action<string> log,
        IProgress<string>? phase,
        CancellationToken ct)
    {
        if (analysis.Target.DiskIsSystem)
            throw new InvalidOperationException("Oprava aktuálneho systémového disku je zablokovaná.");
        if (analysis.WindowsPartitions.Count == 0)
            throw new InvalidOperationException("Na disku sa nenašla Windows inštalácia.");
        if (analysis.IsUefi && analysis.Esp is null)
            throw new InvalidOperationException("Chýba EFI System Partition.");
        if (analysis.IsBios && !File.Exists(SysExe("bootsect.exe")))
            throw new InvalidOperationException("Chýba bootsect.exe.");

        var result = new BootRepairResult();
        var mounted = new Dictionary<DiskPartition, bool>();

        async Task<char> Ensure(DiskPartition partition, string what)
        {
            var had = partition.HasLetter;
            var letter = await BootVolumeAccess.EnsureLetterAsync(partition, log, ct).ConfigureAwait(false);
            if (letter is null)
                throw new InvalidOperationException($"Nepodarilo sa namontovať {what}.");
            if (!had) mounted[partition] = true;
            return letter.Value;
        }

        try
        {
            phase?.Report("Montujem Windows");
            var windows = analysis.WindowsPartitions[0];
            var winLetter = await Ensure(windows, "Windows partíciu").ConfigureAwait(false);
            var winPath = $"{winLetter}:\\Windows";

            char systemLetter;
            DiskPartition systemPartition;
            if (analysis.IsUefi)
            {
                phase?.Report("Montujem ESP");
                systemPartition = analysis.Esp!;
                if (systemPartition.FileSystem != FileSystemKind.Fat32)
                    throw new InvalidOperationException("ESP nie je FAT32; automatická oprava nie je bezpečná.");
                systemLetter = await Ensure(systemPartition, "ESP").ConfigureAwait(false);
            }
            else
            {
                phase?.Report("Montujem systémovú partíciu");
                systemPartition = analysis.SystemPartition ?? analysis.ActivePartition ?? windows;
                systemLetter = await Ensure(systemPartition, "systémovú partíciu").ConfigureAwait(false);
            }

            phase?.Report("Zálohujem boot súbory");
            result.BackupFolder = await BackupAsync(analysis, winLetter, systemLetter, log, ct)
                .ConfigureAwait(false);
            log("Záloha: " + result.BackupFolder);

            if (renewDiskSignature && !analysis.Target.IsGpt)
            {
                phase?.Report("Mením disk signature");
                await RenewMbrSignatureAsync(analysis, log, ct).ConfigureAwait(false);
                log("Disk signature bola zmenená; BCD sa vytvorí znova.");
            }

            if (analysis.IsBios)
            {
                phase?.Report("Nastavujem active partíciu");
                await SetActiveAsync(analysis, systemPartition, log, ct).ConfigureAwait(false);
            }

            phase?.Report(analysis.IsUefi ? "Obnovujem UEFI boot" : "Obnovujem BIOS boot");
            var bcdboot = SysExe("bcdboot.exe");
            var firmware = analysis.IsUefi ? "UEFI" : "BIOS";
            await RunAsync(bcdboot,
                new[] { winPath, "/s", $"{systemLetter}:", "/f", firmware, "/v" },
                log, ct).ConfigureAwait(false);

            if (analysis.IsUefi)
            {
                var efiBootDir = Path.Combine($"{systemLetter}:\\", "EFI", "Boot");
                var fallback = Path.Combine(efiBootDir, "bootx64.efi");
                var bootmgfw = Path.Combine($"{systemLetter}:\\", "EFI", "Microsoft", "Boot", "bootmgfw.efi");
                if (!File.Exists(fallback) && File.Exists(bootmgfw))
                {
                    Directory.CreateDirectory(efiBootDir);
                    File.Copy(bootmgfw, fallback, true);
                    log("Doplnený fallback \\EFI\\Boot\\bootx64.efi.");
                }
            }
            else
            {
                phase?.Report("Opravujem MBR a VBR");
                var bootsect = SysExe("bootsect.exe");
                await RunAsync(bootsect,
                    new[] { "/nt60", $"{systemLetter}:", "/mbr", "/force" },
                    log, ct).ConfigureAwait(false);
            }

            phase?.Report("Overujem výsledok");
            var bcdPath = analysis.IsUefi
                ? Path.Combine($"{systemLetter}:\\", "EFI", "Microsoft", "Boot", "BCD")
                : Path.Combine($"{systemLetter}:\\", "Boot", "BCD");
            var bcdEdit = SysExe("bcdedit.exe");
            var check = await RunAsync(bcdEdit, new[] { "/store", bcdPath, "/enum", "all" },
                log, ct, throwOnError: false).ConfigureAwait(false);
            var hasLoader = check.Output.Contains("winload.efi", StringComparison.OrdinalIgnoreCase) ||
                            check.Output.Contains("winload.exe", StringComparison.OrdinalIgnoreCase);
            var hasBootmgr = analysis.IsUefi
                ? File.Exists(Path.Combine($"{systemLetter}:\\", "EFI", "Microsoft", "Boot", "bootmgfw.efi"))
                : File.Exists(Path.Combine($"{systemLetter}:\\", "bootmgr"));

            result.Success = check.ExitCode == 0 && hasLoader && hasBootmgr;
            result.Summary = result.Success
                ? "Bootovanie bolo obnovené. Overte štart po reštarte a nastavenia BIOS/UEFI."
                : "Boot súbory boli zapísané, ale kontrola BCD nie je úplná. Pozrite protokol.";
            result.Details.Add($"Režim: {firmware}");
            result.Details.Add($"Windows: {winPath}");
            result.Details.Add($"Systémová partícia: {systemLetter}:");
            result.Details.Add($"BCD: {bcdPath}");
            result.Details.Add($"Záloha: {result.BackupFolder}");
            log("");
            log(result.Summary);
            return result;
        }
        finally
        {
            foreach (var (partition, _) in mounted)
                await BootVolumeAccess.ReleaseAsync(partition, true, CancellationToken.None)
                    .ConfigureAwait(false);
        }
    }

    public static async Task<BootRecoveryResult> AddRecoveryAsync(
        BootAnalysis analysis,
        BootRecoveryImage image,
        Action<string> log,
        IProgress<string>? phase,
        CancellationToken ct)
    {
        if (analysis.Target.DiskIsSystem)
            throw new InvalidOperationException("Aktuálny systémový disk je pre zápis zablokovaný.");
        if (image is null)
            throw new InvalidOperationException("Recovery obraz sa nenašiel.");

        var result = new BootRecoveryResult();
        var mounted = new Dictionary<DiskPartition, bool>();

        async Task<char> Ensure(DiskPartition partition, string what)
        {
            var had = partition.HasLetter;
            var letter = await BootVolumeAccess.EnsureLetterAsync(partition, log, ct).ConfigureAwait(false);
            if (letter is null) throw new InvalidOperationException($"Nepodarilo sa namontovať {what}.");
            if (!had) mounted[partition] = true;
            return letter.Value;
        }

        try
        {
            phase?.Report("Montujem partície");
            var windows = analysis.WindowsPartitions[0];
            var winLetter = await Ensure(windows, "Windows partíciu").ConfigureAwait(false);
            var imageLetter = await Ensure(image.Partition, "recovery partíciu").ConfigureAwait(false);

            var bcdPartition = analysis.IsUefi
                ? analysis.Esp!
                : analysis.SystemPartition ?? analysis.ActivePartition ?? windows;
            var bcdLetter = await Ensure(bcdPartition, "systémovú partíciu").ConfigureAwait(false);
            var bcdPath = analysis.IsUefi
                ? Path.Combine($"{bcdLetter}:\\", "EFI", "Microsoft", "Boot", "BCD")
                : Path.Combine($"{bcdLetter}:\\", "Boot", "BCD");
            if (!File.Exists(bcdPath))
                throw new InvalidOperationException("BCD neexistuje. Najprv spustite opravu bootovania.");

            var backup = await BackupAsync(analysis, winLetter, bcdLetter, log, ct).ConfigureAwait(false);
            result.BackupFolder = backup;

            var sdi = FindBootSdi(windows, winLetter, analysis.IsUefi);
            if (sdi is null)
                throw new InvalidOperationException("Nenašiel sa boot.sdi pre recovery ramdisk.");

            var imageRoot = $"{imageLetter}:\\";
            var wimRelative = string.IsNullOrWhiteSpace(image.RelativePath)
                ? (image.Path.StartsWith(imageRoot, StringComparison.OrdinalIgnoreCase)
                    ? image.Path[imageRoot.Length..]
                    : Path.GetFileName(image.Path))
                : image.RelativePath;
            if (!wimRelative.StartsWith('\\')) wimRelative = "\\" + wimRelative;
            if (!File.Exists(Path.Combine(imageRoot, wimRelative.TrimStart('\\'))))
                throw new InvalidOperationException("Recovery WIM sa po namontovaní nenašiel.");

            var sdiRoot = sdi.StartsWith($"{winLetter}:\\", StringComparison.OrdinalIgnoreCase)
                ? $"{winLetter}:\\"
                : $"{imageLetter}:\\";
            var sdiRelative = sdi.StartsWith(sdiRoot, StringComparison.OrdinalIgnoreCase)
                ? sdi[sdiRoot.Length..]
                : Path.GetFileName(sdi);
            if (!sdiRelative.StartsWith('\\')) sdiRelative = "\\" + sdiRelative;

            var bcdedit = SysExe("bcdedit.exe");
            var ramdisk = "{ramdiskoptions}";
            // {ramdiskoptions} nemusí existovať. Vytvoríme ho idempotentne a vždy
            // nastavíme cestu k boot.sdi, aby nový záznam neodkazoval na prázdny objekt.
            var create = await RunAsync(bcdedit,
                new[] { "/store", bcdPath, "/create", ramdisk, "/d", "Ramdisk Options" },
                log, ct, throwOnError: false).ConfigureAwait(false);
            if (create.ExitCode != 0 &&
                !create.Output.Contains("already exists", StringComparison.OrdinalIgnoreCase) &&
                !create.Output.Contains("už existuje", StringComparison.OrdinalIgnoreCase))
                log("Ramdisk options: " + create.Output.Trim());

            await RunAsync(bcdedit,
                new[] { "/store", bcdPath, "/set", ramdisk, "ramdisksdidevice", $"partition={winLetter}:" },
                log, ct).ConfigureAwait(false);
            await RunAsync(bcdedit,
                new[] { "/store", bcdPath, "/set", ramdisk, "ramdisksdipath", sdiRelative },
                log, ct).ConfigureAwait(false);

            var optionsCheck = await RunAsync(bcdedit,
                new[] { "/store", bcdPath, "/enum", ramdisk },
                log, ct, throwOnError: false).ConfigureAwait(false);
            if (optionsCheck.ExitCode != 0 ||
                !optionsCheck.Output.Contains("ramdisksdi", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Ramdisk options sa nepodarilo nastaviť.");

            var title = image.IsWindowsRe
                ? "Windows RE"
                : $"OEM Recovery ({analysis.Vendor})";
            var created = await RunAsync(bcdedit,
                new[] { "/store", bcdPath, "/create", "/d", title, "/application", "osloader" },
                log, ct).ConfigureAwait(false);
            var guid = ExtractGuid(created.Output);
            if (guid is null)
                throw new InvalidOperationException("Nepodarilo sa vytvoriť BCD záznam recovery.");

            var ramdiskPath = $"ramdisk=[{imageLetter}:]{wimRelative},{{ramdiskoptions}}";
            await RunAsync(bcdedit,
                new[] { "/store", bcdPath, "/set", guid, "device", ramdiskPath },
                log, ct).ConfigureAwait(false);
            await RunAsync(bcdedit,
                new[] { "/store", bcdPath, "/set", guid, "osdevice", ramdiskPath },
                log, ct).ConfigureAwait(false);
            await RunAsync(bcdedit,
                new[] { "/store", bcdPath, "/set", guid, "path", analysis.IsUefi
                    ? "\\windows\\system32\\winload.efi"
                    : "\\windows\\system32\\winload.exe" },
                log, ct).ConfigureAwait(false);
            await RunAsync(bcdedit,
                new[] { "/store", bcdPath, "/set", guid, "systemroot", "\\windows" },
                log, ct).ConfigureAwait(false);
            await RunAsync(bcdedit,
                new[] { "/store", bcdPath, "/set", guid, "winpe", "yes" },
                log, ct).ConfigureAwait(false);
            await RunAsync(bcdedit,
                new[] { "/store", bcdPath, "/set", guid, "detecthal", "yes" },
                log, ct).ConfigureAwait(false);
            await RunAsync(bcdedit,
                new[] { "/store", bcdPath, "/set", "{bootmgr}", "displayorder", guid, "/addlast" },
                log, ct).ConfigureAwait(false);

            result.EntryGuid = guid;
            result.Success = true;
            result.Summary = $"{title} bola pridaná do boot menu. Záloha: {backup}";
            log(result.Summary);
            return result;
        }
        finally
        {
            foreach (var (partition, _) in mounted)
                await BootVolumeAccess.ReleaseAsync(partition, true, CancellationToken.None)
                    .ConfigureAwait(false);
        }
    }

    public static async Task<BootRepairResult> ConvertMbrToGptAsync(
        BootAnalysis analysis,
        Action<string> log,
        IProgress<string>? phase,
        CancellationToken ct)
    {
        if (analysis.Target.DiskIsSystem)
            throw new InvalidOperationException("Aktuálny systémový disk sa nekonvertuje.");
        if (analysis.Target.IsGpt)
            throw new InvalidOperationException("Disk je už GPT.");
        if (analysis.WindowsPartitions.Count == 0)
            throw new InvalidOperationException("Nenašla sa Windows inštalácia.");
        var mbr2gpt = SysExe("mbr2gpt.exe");
        if (!File.Exists(mbr2gpt))
            throw new InvalidOperationException("Chýba mbr2gpt.exe.");

        var result = new BootRepairResult();
        phase?.Report("Zálohujem MBR");
        result.BackupFolder = await BackupAsync(analysis,
            analysis.WindowsPartitions[0].Letter, analysis.ActivePartition?.Letter ?? 'C', log, ct)
            .ConfigureAwait(false);

        phase?.Report("Validujem konverziu MBR → GPT");
        var validate = await RunAsync(mbr2gpt,
            new[] { "/validate", $"/disk:{analysis.Target.DiskNumber}", "/allowFullOS" },
            log, ct, throwOnError: false).ConfigureAwait(false);
        if (validate.ExitCode != 0)
            throw new InvalidOperationException("mbr2gpt validácia zlyhala. Disk nie je vhodný na konverziu.");

        phase?.Report("Konvertujem MBR → GPT");
        var convert = await RunAsync(mbr2gpt,
            new[] { "/convert", $"/disk:{analysis.Target.DiskNumber}", "/allowFullOS" },
            log, ct, throwOnError: false).ConfigureAwait(false);
        if (convert.ExitCode != 0)
            throw new InvalidOperationException("mbr2gpt konverzia zlyhala. Pozrite protokol.");

        result.Success = true;
        result.Summary = "Disk bol prevedený na GPT. Teraz je potrebné obnoviť UEFI boot súbory a nastaviť BIOS na UEFI.";
        result.Details.Add("Po konverzii vypnite CSM/Legacy, zapnite UEFI a nastavte boot z Windows Boot Manager.");
        log(result.Summary);
        return result;
    }

    private static async Task<string> BackupAsync(BootAnalysis analysis, char winLetter,
        char systemLetter, Action<string> log, CancellationToken ct)
    {
        var folder = Path.Combine(AppPaths.BootBackups, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);

        var bcd = analysis.IsUefi
            ? Path.Combine($"{systemLetter}:\\", "EFI", "Microsoft", "Boot", "BCD")
            : Path.Combine($"{systemLetter}:\\", "Boot", "BCD");
        if (File.Exists(bcd))
            File.Copy(bcd, Path.Combine(folder, "BCD"), true);

        var bootManager = analysis.IsUefi
            ? Path.Combine($"{systemLetter}:\\", "EFI", "Microsoft", "Boot", "bootmgfw.efi")
            : Path.Combine($"{systemLetter}:\\", "bootmgr");
        if (File.Exists(bootManager))
            File.Copy(bootManager, Path.Combine(folder, Path.GetFileName(bootManager)), true);

        try
        {
            using var disk = new RawDisk(analysis.Target.DiskNumber, analysis.Target.SectorSize,
                analysis.Target.DiskSize);
            File.WriteAllBytes(Path.Combine(folder, "mbr.bin"), disk.ReadBytes(0, 512));
            var sys = analysis.ActivePartition ?? analysis.SystemPartition;
            if (sys is not null)
                File.WriteAllBytes(Path.Combine(folder, "vbr.bin"), disk.ReadBytes(sys.Offset, 512));
        }
        catch (Exception ex)
        {
            log("Nepodarilo sa zálohovať MBR/VBR: " + ex.Message);
            throw new InvalidOperationException("Záloha MBR/VBR zlyhala; oprava sa nespustí.", ex);
        }

        var manifest = new StringBuilder();
        manifest.AppendLine("Zdenko, zachráň ma! — záloha bootovania");
        manifest.AppendLine($"Čas: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        manifest.AppendLine($"Disk: {analysis.Target.DiskNumber} {analysis.Target.Model}");
        manifest.AppendLine($"Štýl: {analysis.Target.PartitionStyle}");
        manifest.AppendLine($"Režim: {(analysis.IsUefi ? "UEFI" : "BIOS")}");
        manifest.AppendLine($"Windows: {winLetter}:\\Windows");
        File.WriteAllText(Path.Combine(folder, "manifest.txt"), manifest.ToString(), Encoding.UTF8);
        return folder;
    }

    private static async Task SetActiveAsync(BootAnalysis analysis, DiskPartition target,
        Action<string> log, CancellationToken ct)
    {
        var script = new StringBuilder();
        foreach (var p in analysis.Partitions.Where(p => p.IsActive && p.PartitionNumber != target.PartitionNumber))
            script.AppendLine($"Set-Partition -DiskNumber {p.DiskNumber} -PartitionNumber {p.PartitionNumber} -IsActive $false -ErrorAction SilentlyContinue");
        script.AppendLine($"Set-Partition -DiskNumber {target.DiskNumber} -PartitionNumber {target.PartitionNumber} -IsActive $true -ErrorAction Stop");
        var r = await Ps.RunAsync(script.ToString(), ct, 30000).ConfigureAwait(false);
        if (r is null) throw new InvalidOperationException("Nepodarilo sa nastaviť active partíciu.");
        target.IsActive = true;
        log($"Active partícia nastavená na {target.PartitionNumber}.");
    }

    private static async Task RenewMbrSignatureAsync(BootAnalysis analysis, Action<string> log,
        CancellationToken ct)
    {
        if (analysis.Target.DiskIsSystem)
            throw new InvalidOperationException("Signature aktuálneho systémového disku sa nemení.");
        var signature = Random.Shared.Next(1, int.MaxValue).ToString("X8");
        var script = Path.Combine(Path.GetTempPath(), $"zzm-diskpart-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(script, $"select disk {analysis.Target.DiskNumber}\r\nuniqueid disk id={signature}\r\n");
            var r = await ProcessRunner.RunAsync(SysExe("diskpart.exe"),
                new[] { "/s", script }, log, ct, 60000).ConfigureAwait(false);
            if (r.ExitCode != 0)
                throw new InvalidOperationException("Zmena disk signature zlyhala.");
            log($"Nová MBR signature: {signature}");
        }
        finally
        {
            try { File.Delete(script); } catch { }
        }
    }

    private static string? FindBootSdi(DiskPartition windows, char winLetter, bool uefi)
    {
        var candidates = new List<string>
        {
            Path.Combine($"{winLetter}:\\", "Windows", "Boot", "DVD", uefi ? "EFI" : "PCAT", "boot.sdi"),
            Path.Combine($"{winLetter}:\\", "Boot", "boot.sdi"),
            Path.Combine($"{winLetter}:\\", "boot.sdi")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<ProcessResult> RunAsync(string exe, string[] args, Action<string> log,
        CancellationToken ct, bool throwOnError = true)
    {
        log("");
        log($"> {Path.GetFileName(exe)} {string.Join(" ", args)}");
        if (!File.Exists(exe))
            throw new InvalidOperationException($"Chýba {Path.GetFileName(exe)}.");
        var result = await ProcessRunner.RunAsync(exe, args, log, ct).ConfigureAwait(false);
        if (throwOnError && result.ExitCode != 0)
            throw new InvalidOperationException($"{Path.GetFileName(exe)} zlyhal (exit {result.ExitCode}).");
        return result;
    }

    internal static string? ExtractGuid(string output)
    {
        var match = System.Text.RegularExpressions.Regex.Match(output,
            "\\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\\}");
        return match.Success ? match.Value : null;
    }
}
