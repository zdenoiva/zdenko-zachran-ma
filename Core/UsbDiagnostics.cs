using System.Text;
using NtfsRescue.Util;

namespace NtfsRescue.Core;

/// <summary>Diagnostický report pre USB mostíky, SMART a identitu diskov.</summary>
public static class UsbDiagnostics
{
    public static async Task<string> BuildAsync(CancellationToken ct)
    {
        var text = new StringBuilder();
        text.AppendLine("Zdenko, zachráň ma! — USB/SMART diagnostika");
        text.AppendLine($"Dátum: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"Verzia: {AppInfo.VersionDisplay}");
        text.AppendLine($"Windows: {Environment.OSVersion}");
        text.AppendLine("Režim: len INQUIRY, VPD 0x80 a ATA PASS-THROUGH " +
                        "(REPORT LUNS/VPD 0x83 sa zámerne neposielajú)");
        text.AppendLine();

        // Všetky fyzické disky priamo z Win32_DiskDrive. Get-Disk niekedy
        // vynechá prázdny/RAW/offline disk, a práve ten potrebujeme otestovať.
        var diskDoc = await Ps.RunJsonAsync(@"
Get-CimInstance Win32_DiskDrive -ErrorAction SilentlyContinue | ForEach-Object {
  $id = [string]$_.PNPDeviceID
  $chain = @()
  $cur = $id
  for ($i = 0; $i -lt 6 -and $cur; $i++) {
    $chain += $cur
    $cur = [string](Get-PnpDeviceProperty -InstanceId $cur -KeyName 'DEVPKEY_Device_Parent' -ErrorAction SilentlyContinue).Data
  }
  [pscustomobject]@{
    Index = [int]$_.Index
    Model = [string]$_.Model
    Serial = [string]$_.SerialNumber
    Interface = [string]$_.InterfaceType
    Media = [string]$_.MediaType
    Size = [int64]$_.Size
    Partitions = [int]$_.Partitions
    Pnp = $id
    Chain = ($chain -join ' | ')
  }
} | ConvertTo-Json -Compress -Depth 4", ct, 30000).ConfigureAwait(false);

        var disks = new List<(int Index, string Model, string Serial, string Interface,
            string Media, long Size, int Partitions, string Pnp, string Chain)>();
        if (diskDoc is not null)
        {
            foreach (var d in Ps.Objects(diskDoc))
            {
                int index = Ps.Int(d, "Index");
                if (index < 0) continue;
                disks.Add((index, Ps.Str(d, "Model"), Ps.Str(d, "Serial"),
                    Ps.Str(d, "Interface"), Ps.Str(d, "Media"), Ps.Long(d, "Size"),
                    Ps.Int(d, "Partitions"), Ps.Str(d, "Pnp"), Ps.Str(d, "Chain")));
            }
        }
        disks.Sort((a, b) => a.Index.CompareTo(b.Index));

        text.AppendLine("=== Windows disky (Win32_DiskDrive) ===");
        foreach (var d in disks)
        {
            text.AppendLine($"Disk {d.Index}: model='{d.Model}' SN='{d.Serial}' " +
                            $"rozhranie={d.Interface} médium='{d.Media}' " +
                            $"veľkosť={d.Size} partície={d.Partitions}");
            text.AppendLine($"  PNP: {d.Pnp}");
            text.AppendLine($"  Reťazec: {d.Chain}");
        }
        if (disks.Count == 0) text.AppendLine("žiadne disky");
        text.AppendLine();

        var usb = await Ps.RunAsync(
            "Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | " +
            "Where-Object { $_.InstanceId -like 'USB\\VID_*' } | " +
            "Select-Object Status,Class,FriendlyName,InstanceId | " +
            "Format-Table -AutoSize | Out-String -Width 300",
            ct, 20000).ConfigureAwait(false);
        text.AppendLine("=== USB zariadenia (VID/PID) ===");
        text.AppendLine(string.IsNullOrWhiteSpace(usb) ? "nedostupné" : usb.Trim());
        text.AppendLine();

        var getDisk = await Ps.RunAsync(
            "Get-Disk -ErrorAction SilentlyContinue | Select-Object Number,FriendlyName," +
            "SerialNumber,BusType,PartitionStyle,OperationalStatus,HealthStatus,Size," +
            "IsOffline,IsReadOnly | Format-Table -AutoSize | Out-String -Width 300",
            ct, 30000).ConfigureAwait(false);
        text.AppendLine("=== Get-Disk ===");
        text.AppendLine(string.IsNullOrWhiteSpace(getDisk) ? "nedostupné" : getDisk.Trim());
        text.AppendLine();

        var physical = await Ps.RunAsync(
            "Get-PhysicalDisk -ErrorAction SilentlyContinue | Select-Object DeviceId," +
            "FriendlyName,SerialNumber,BusType,MediaType,HealthStatus,OperationalStatus," +
            "Size | Format-Table -AutoSize | Out-String -Width 300",
            ct, 30000).ConfigureAwait(false);
        text.AppendLine("=== Get-PhysicalDisk ===");
        text.AppendLine(string.IsNullOrWhiteSpace(physical) ? "nedostupné" : physical.Trim());
        text.AppendLine();

        bool anyAtaSuccess = false;
        bool anyInvalidOpcode = false;

        foreach (var disk in disks)
        {
            ct.ThrowIfCancellationRequested();

            text.AppendLine($"=== Disk {disk.Index}: {disk.Model} ===");
            text.AppendLine($"Veľkosť: {Fmt.Bytes(disk.Size)}; rozhranie: {disk.Interface}; " +
                            $"médium: {disk.Media}; SN: {disk.Serial}");

            bool likelyNvme = disk.Model.Contains("NVME", StringComparison.OrdinalIgnoreCase) ||
                              disk.Pnp.Contains("VEN_NVME", StringComparison.OrdinalIgnoreCase) ||
                              disk.Pnp.StartsWith("SCSI\\DISK&VEN_NVME", StringComparison.OrdinalIgnoreCase);

            foreach (var line in AtaSmart.ScsiInfo(disk.Index, disk.Model))
                text.AppendLine("  " + line);

            if (likelyNvme)
            {
                text.AppendLine("  ATA/SMART: NVMe disk, ATA PASS-THROUGH sa neposiela.");
                text.AppendLine();
                continue;
            }

            var identify = AtaSmart.Probe(disk.Index, smart: false, expectedModel: disk.Model);
            text.AppendLine($"  ATA IDENTIFY: {(identify.Success.Length > 0 ? "ÁNO — " + identify.Success : "NIE")}" +
                            (identify.Error.Length > 0 ? $" ({identify.Error})" : ""));
            AppendProbe(text, identify);
            if (identify.Page is not null && identify.Success.Length > 0)
            {
                var (serial, model) = AtaSmart.ParseIdentifyPublic(identify.Page);
                text.AppendLine($"    model='{model}' serial='{serial}'");
            }

            var smart = AtaSmart.Probe(disk.Index, smart: true, expectedModel: disk.Model);
            text.AppendLine($"  S.M.A.R.T.: {(smart.Success.Length > 0 ? "ÁNO — " + smart.Success : "NIE")}" +
                            (smart.Error.Length > 0 ? $" ({smart.Error})" : ""));
            AppendProbe(text, smart);

            if (smart.Page is not null && smart.Success.Length > 0)
            {
                anyAtaSuccess = true;
                var attributes = AtaSmart.ParseAttributes(smart.Page);
                text.AppendLine($"    atribútov={attributes.Count}");
                foreach (var attribute in attributes)
                    text.AppendLine($"      {attribute.Id,3} value={attribute.Value,3} " +
                                    $"worst={attribute.Worst,3} raw={attribute.Raw}");
            }

            anyInvalidOpcode |= smart.Lines.Any(l =>
                l.Asc.Contains("asc=20", StringComparison.OrdinalIgnoreCase) ||
                l.Asc.Contains("asc=24", StringComparison.OrdinalIgnoreCase));

            text.AppendLine();
        }

        text.AppendLine("=== Záver ===");
        if (anyAtaSuccess)
            text.AppendLine("ATA PASS-THROUGH funguje aspoň na jednom disku — protokol je uvedený vyššie.");
        else if (anyInvalidOpcode)
            text.AppendLine("Mostík odmietol ATA PASS-THROUGH (SCSI: neplatný príkaz / pole). " +
                            "Skontrolujte prepínač režimu boxu (JBOD/RAID/Clone) a či sú oba disky " +
                            "vložené. V režime RAID/Clone box SMART jednotlivých diskov neposkytuje.");
        else
            text.AppendLine("ATA PASS-THROUGH sa nepodarilo otvoriť. Skontrolujte, či program beží " +
                            "ako správca a či disk nie je používaný iným programom.");

        return text.ToString();
    }

    private static void AppendProbe(StringBuilder text, AtaSmart.AtaProbe probe)
    {
        text.AppendLine($"    adresa={probe.Address} directStruct={probe.DirectStructSize} " +
                        $"nonDirectStruct={probe.NonDirectStructSize}");
        foreach (var line in probe.Lines)
        {
            text.AppendLine($"    {line.Name,-13} {line.Address,-7} {line.Transport,-10} " +
                            $"{line.Cdb,-36} {line.Ms,5} ms  {line.Outcome}" +
                            (line.Asc.Length > 0 ? $"  {line.Asc}" : "") +
                            (line.Sense.Length > 0 && line.Outcome != "OK" ? $"  sense={line.Sense}" : ""));
        }
    }
}
