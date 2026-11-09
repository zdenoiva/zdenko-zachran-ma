using NtfsRescue.Util;

namespace NtfsRescue.Core;

public sealed class DiskInfo
{
    public int Number;
    public string Model = "";
    public string Serial = "";
    public string BusType = "";
    public string PartitionStyle = "";
    public string Guid = "";
    public string Signature = "";
    public string Health = "";
    public string Status = "";
    public long Size;
    public bool IsBoot;
    public bool IsSystem;
    public bool IsOffline;
    public bool IsReadOnly;

    public bool IsCurrentSystem => IsSystem || IsBoot;
    public string Display =>
        $"Disk {Number} · {Model} · {Fmt.Bytes(Size)} · " +
        $"{(string.IsNullOrWhiteSpace(PartitionStyle) ? "?" : PartitionStyle)} · {BusType}" +
        (IsCurrentSystem ? " · SYSTÉMOVÝ" : "");

    public override string ToString() => Display;
}

public static class DiskInfoCatalog
{
    public static async Task<List<DiskInfo>> LoadAsync(CancellationToken ct)
    {
        var doc = await Ps.RunJsonAsync(
            "Get-Disk -ErrorAction SilentlyContinue | Select-Object Number,FriendlyName,SerialNumber,BusType," +
            "PartitionStyle,Guid,Signature,HealthStatus,OperationalStatus,Size,IsBoot,IsSystem,IsOffline,IsReadOnly " +
            "| ConvertTo-Json -Compress",
            ct, 30000).ConfigureAwait(false);

        var list = new List<DiskInfo>();
        if (doc is not null)
        {
            foreach (var obj in Ps.Objects(doc))
            {
                var disk = new DiskInfo
                {
                    Number = Ps.Int(obj, "Number"),
                    Model = Ps.Str(obj, "FriendlyName"),
                    Serial = Ps.Str(obj, "SerialNumber"),
                    BusType = Ps.Str(obj, "BusType"),
                    PartitionStyle = Ps.Str(obj, "PartitionStyle"),
                    Guid = Ps.Str(obj, "Guid"),
                    Signature = Ps.Str(obj, "Signature"),
                    Health = Ps.Str(obj, "HealthStatus"),
                    Status = Ps.Str(obj, "OperationalStatus"),
                    Size = Ps.Long(obj, "Size"),
                    IsBoot = Ps.Bool(obj, "IsBoot"),
                    IsSystem = Ps.Bool(obj, "IsSystem"),
                    IsOffline = Ps.Bool(obj, "IsOffline"),
                    IsReadOnly = Ps.Bool(obj, "IsReadOnly")
                };
                if (disk.Number >= 0 && disk.Size > 0) list.Add(disk);
            }
        }

        if (list.Count == 0)
        {
            var partitions = await DiskCatalog.LoadAllAsync(ct).ConfigureAwait(false);
            foreach (var group in partitions.GroupBy(p => p.DiskNumber))
            {
                var first = group.First();
                list.Add(new DiskInfo
                {
                    Number = first.DiskNumber,
                    Model = first.Model,
                    Serial = first.Serial,
                    BusType = first.BusType,
                    PartitionStyle = first.PartitionStyle,
                    Guid = first.DiskGuid,
                    Signature = first.DiskSignature,
                    Size = first.DiskSize,
                    IsBoot = first.DiskIsBoot,
                    IsSystem = first.DiskIsSystem,
                    IsOffline = first.DiskIsOffline,
                    IsReadOnly = first.DiskIsReadOnly
                });
            }
        }

        list.Sort((a, b) => a.Number.CompareTo(b.Number));
        return list;
    }
}
