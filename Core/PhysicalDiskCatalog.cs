using NtfsRescue.Util;

namespace NtfsRescue.Core;

public sealed record PhysicalDisk(int Number, string Model, string Serial, string Bus, long Size);

/// <summary>
/// Zoznam fyzických diskov priamo z Win32_DiskDrive. Get-Disk niekedy
/// vynechá prázdny, RAW alebo offline disk — a práve ten treba zobraziť.
/// </summary>
public static class PhysicalDiskCatalog
{
    public static async Task<Dictionary<int, PhysicalDisk>> LoadAsync(CancellationToken ct)
    {
        var result = new Dictionary<int, PhysicalDisk>();
        var doc = await Ps.RunJsonAsync(
            "Get-CimInstance Win32_DiskDrive -ErrorAction SilentlyContinue | " +
            "Select-Object Index,Model,SerialNumber,InterfaceType,MediaType,Size | " +
            "ConvertTo-Json -Compress",
            ct, 20000).ConfigureAwait(false);
        if (doc is null) return result;

        foreach (var obj in Ps.Objects(doc))
        {
            int number = Ps.Int(obj, "Index");
            if (number < 0) continue;

            long size = Ps.Long(obj, "Size");
            var bus = Ps.Str(obj, "InterfaceType");
            var media = Ps.Str(obj, "MediaType");
            if (!bus.Equals("USB", StringComparison.OrdinalIgnoreCase) &&
                media.Contains("External", StringComparison.OrdinalIgnoreCase))
                bus = "USB";

            result[number] = new PhysicalDisk(number, Ps.Str(obj, "Model"),
                Ps.Str(obj, "SerialNumber"), bus, size);
        }
        return result;
    }
}
