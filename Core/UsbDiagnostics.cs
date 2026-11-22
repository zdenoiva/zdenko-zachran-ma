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
        text.AppendLine();

        var pnp = await Ps.RunAsync(
            "Get-CimInstance Win32_DiskDrive -ErrorAction SilentlyContinue | " +
            "Select-Object Index,Model,SerialNumber,InterfaceType,MediaType,PNPDeviceID,Size | " +
            "Format-List | Out-String -Width 300",
            ct, 20000).ConfigureAwait(false);
        text.AppendLine("=== Windows PnP ===");
        text.AppendLine(string.IsNullOrWhiteSpace(pnp) ? "nedostupné" : pnp.Trim());
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

        List<DiskInfo> disks;
        try
        {
            disks = await DiskInfoCatalog.LoadAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            text.AppendLine("Disky sa nepodarilo načítať: " + ex.Message);
            return text.ToString();
        }

        foreach (var disk in disks)
        {
            ct.ThrowIfCancellationRequested();
            text.AppendLine($"=== Disk {disk.Number}: {disk.Model} ===");
            text.AppendLine($"Veľkosť: {Fmt.Bytes(disk.Size)}; zbernica: {disk.BusType}; " +
                            $"štýl: {disk.PartitionStyle}; SN: {Fmt.Serial(disk.Serial)}");

            var inquiry = AtaSmart.ReadInquiry(disk.Number);
            text.AppendLine($"SCSI INQUIRY: model={inquiry?.Model ?? ""} " +
                            $"serial={inquiry?.Serial ?? ""}");

            var identify = AtaSmart.ReadIdentify(disk.Number);
            text.AppendLine($"ATA IDENTIFY: model={identify?.Model ?? ""} " +
                            $"serial={identify?.Serial ?? ""} " +
                            $"protocol={AtaSmart.LastProtocol}");

            var smart = SmartInfoService.Query(disk.Number, ct, 6000);
            text.AppendLine($"S.M.A.R.T.: available={smart.Available} " +
                            $"attributes={smart.Attributes.Count} score={smart.HealthScore} " +
                            $"protocol={AtaSmart.LastProtocol} address={AtaSmart.LastAddress}");
            text.AppendLine($"S.M.A.R.T. text: {smart.DisplayText}");
            text.AppendLine($"S.M.A.R.T. error: {AtaSmart.LastError}");

            foreach (var attribute in smart.Attributes)
                text.AppendLine($"  {attribute.Id,3} {attribute.Name,-32} " +
                                $"value={attribute.Value,3} worst={attribute.Worst,3} raw={attribute.Raw}");
            text.AppendLine();
        }

        return text.ToString();
    }
}
