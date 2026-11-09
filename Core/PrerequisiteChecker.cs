using System.Security.Principal;
using NtfsRescue.Util;

namespace NtfsRescue.Core;

public sealed class PrerequisiteItem
{
    public string Key = "";
    public string Title = "";
    public string Detail = "";
    public bool Required = true;
    public bool Ok;
    public bool CanFix;
    public string Status => Ok ? "OK" : Required ? "CHÝBA" : "obmedzené";
}

public static class PrerequisiteChecker
{
    private static string SystemExe(string name) => Path.Combine(Environment.SystemDirectory, name);

    private static string PowerShellPath =>
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    public static async Task<List<PrerequisiteItem>> CheckAsync(Action<string>? log = null,
        CancellationToken ct = default)
    {
        var items = new List<PrerequisiteItem>();

        // 1. Windows x64
        var build = Environment.OSVersion.Version.Build;
        var winOk = Environment.Is64BitOperatingSystem && build >= 17763;
        items.Add(new PrerequisiteItem
        {
            Key = "windows",
            Title = "Windows 10/11 64-bit",
            Detail = winOk
                ? $"Windows build {build}, 64-bit"
                : $"Zistený Windows build {build}, 64-bit={Environment.Is64BitOperatingSystem}. " +
                  "Program potrebuje Windows 10/11 x64.",
            Required = true,
            Ok = winOk
        });

        // 2. Administrátor
        var admin = IsAdministrator();
        items.Add(new PrerequisiteItem
        {
            Key = "admin",
            Title = "Administrátorské oprávnenia",
            Detail = admin
                ? "Program beží ako administrátor."
                : "Spustite program cez „Spustiť ako správca“.",
            Required = true,
            Ok = admin
        });

        // 3. Windows PowerShell
        var psOk = false;
        var psDetail = "powershell.exe sa nenašiel.";
        if (File.Exists(PowerShellPath))
        {
            if (log is not null) log("Kontrolujem Windows PowerShell…");
            var r = await ProcessRunner.RunAsync(PowerShellPath,
                new[] { "-NoProfile", "-NonInteractive", "-Command", "$PSVersionTable.PSVersion.ToString()" },
                null, ct, 20000).ConfigureAwait(false);
            var text = r.Output.Trim();
            psOk = r.ExitCode == 0 && text.Length > 0;
            psDetail = psOk ? $"Verzia {text}" : "PowerShell sa nepodarilo spustiť.";
        }
        items.Add(new PrerequisiteItem
        {
            Key = "powershell",
            Title = "Windows PowerShell 5.1",
            Detail = psDetail,
            Required = true,
            Ok = psOk,
            CanFix = !psOk
        });

        // 4. Storage cmdlets
        var storageNames = new[]
        {
            "Get-Disk", "Get-Partition", "Get-Volume", "Get-PhysicalDisk",
            "Get-StorageReliabilityCounter"
        };
        var storageOk = false;
        var storageDetail = "Storage modul nie je dostupný.";
        if (psOk)
        {
            if (log is not null) log("Kontrolujem Storage cmdlety…");
            var r = await ProcessRunner.RunAsync(PowerShellPath,
                new[]
                {
                    "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command",
                    "Get-Command Get-Disk,Get-Partition,Get-Volume,Get-PhysicalDisk,Get-StorageReliabilityCounter " +
                    "-ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name"
                },
                null, ct, 25000).ConfigureAwait(false);
            var found = r.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = storageNames.Where(n => !found.Contains(n)).ToArray();
            storageOk = missing.Length == 0;
            storageDetail = storageOk
                ? "Get-Disk, Get-Partition, Get-Volume, Get-PhysicalDisk, S.M.A.R.T."
                : "Chýba: " + string.Join(", ", missing);
        }
        items.Add(new PrerequisiteItem
        {
            Key = "storage",
            Title = "Storage a S.M.A.R.T. cmdlety",
            Detail = storageDetail,
            Required = true,
            Ok = storageOk,
            CanFix = !storageOk
        });

        // 5. Systémové nástroje
        var tools = new[]
        {
            ("chkdsk.exe", "oprava NTFS/FAT32"),
            ("mountvol.exe", "pripojenie volume"),
            ("takeown.exe", "obnovenie vlastníka"),
            ("icacls.exe", "obnovenie ACL"),
            ("shutdown.exe", "reštart po oprave"),
            ("cmd.exe", "spúšťanie nástrojov"),
            ("bcdboot.exe", "obnova boot súborov"),
            ("bcdedit.exe", "úprava BCD"),
            ("bootsect.exe", "oprava MBR/VBR"),
            ("reagentc.exe", "Windows RE")
        };
        var missingTools = tools.Where(t => !File.Exists(SystemExe(t.Item1))).Select(t => t.Item1).ToArray();
        items.Add(new PrerequisiteItem
        {
            Key = "tools",
            Title = "Systémové nástroje Windows",
            Detail = missingTools.Length == 0
                ? string.Join(", ", tools.Select(t => t.Item1))
                : "Chýba: " + string.Join(", ", missingTools),
            Required = true,
            Ok = missingTools.Length == 0,
            CanFix = missingTools.Length > 0
        });

        // 6. Konverzia MBR -> GPT (voliteľné)
        var mbr2gpt = File.Exists(SystemExe("mbr2gpt.exe"));
        items.Add(new PrerequisiteItem
        {
            Key = "mbr2gpt",
            Title = "Konverzia MBR → GPT",
            Detail = mbr2gpt
                ? "mbr2gpt.exe je dostupný."
                : "mbr2gpt.exe chýba; konverzia MBR → GPT nebude dostupná.",
            Required = false,
            Ok = mbr2gpt
        });

        // 7. S.M.A.R.T. (voliteľné)
        var smartOk = storageOk;
        var smartDetail = storageOk
            ? "Dostupné cez Windows Storage (pri USB mostíkoch môže byť obmedzené)."
            : "S.M.A.R.T. bude nedostupné.";
        items.Add(new PrerequisiteItem
        {
            Key = "smart",
            Title = "S.M.A.R.T. a zdravie disku",
            Detail = smartDetail,
            Required = false,
            Ok = smartOk
        });

        log?.Invoke("Kontrola prostredia dokončená.");
        return items;
    }

    public static async Task<bool> ApplyFixAsync(PrerequisiteItem item, Action<string> log,
        CancellationToken ct)
    {
        try
        {
            switch (item.Key)
            {
                case "powershell":
                {
                    log("Zapínam Windows PowerShell (DISM)…");
                    var dism = SystemExe("dism.exe");
                    foreach (var feature in new[] { "MicrosoftWindowsPowerShellV2", "MicrosoftWindowsPowerShellV2Root" })
                    {
                        await ProcessRunner.RunAsync(dism,
                            new[] { "/Online", "/Enable-Feature", "/FeatureName:" + feature, "/All", "/NoRestart" },
                            log, ct).ConfigureAwait(false);
                    }
                    return true;
                }
                case "storage":
                case "tools":
                {
                    log("Spúšťam DISM RestoreHealth…");
                    var dism = SystemExe("dism.exe");
                    await ProcessRunner.RunAsync(dism,
                        new[] { "/Online", "/Cleanup-Image", "/RestoreHealth" }, log, ct).ConfigureAwait(false);

                    log("Spúšťam SFC /scannow…");
                    var sfc = SystemExe("sfc.exe");
                    await ProcessRunner.RunAsync(sfc, new[] { "/scannow" }, log, ct).ConfigureAwait(false);
                    return true;
                }
                default:
                    log("Tento krok sa nedá doinštalovať automaticky.");
                    return false;
            }
        }
        catch (Exception ex)
        {
            log("Inštalácia zlyhala: " + ex.Message);
            return false;
        }
    }

    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
