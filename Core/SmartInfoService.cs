using System.Text.Json;
using NtfsRescue.Util;

namespace NtfsRescue.Core;

public enum SmartRating
{
    Unknown,
    Good,
    Caution,
    Bad
}

public sealed class SmartAttribute
{
    public int Id;
    public string Name = "";
    public int Value;
    public int Worst;
    public long Raw;
}

public sealed class SmartInfo
{
    public bool Available;
    public bool Nvme;
    public string Model = "";
    public string MediaType = "";
    public string Interface = "";
    public string Health = "";
    public string Status = "";
    public long PowerOnHours;
    public long PowerOnDays;
    public long PowerCycles;
    public long UnsafeShutdowns;
    public long Temperature;
    public long Wear;
    public long RemainingLife = -1;
    public long ReadErrorsTotal;
    public long WriteErrorsTotal;
    public long ReadErrorsUncorrected;
    public long WriteErrorsUncorrected;
    public long ReallocatedSectors;
    public long PendingSectors;
    public long UncorrectableSectors;
    public long CrcErrors;
    public long MediaErrors;
    public long ErrorLogEntries;
    public long AvailableSpare = -1;
    public long SpareThreshold = -1;
    public int CriticalWarning;
    public ulong HostReadBytes;
    public ulong HostWriteBytes;
    public bool PredictFailure;
    public bool HasAlert;
    public bool ReplacementRecommended;
    public SmartRating Rating = SmartRating.Unknown;
    public int HealthScore = -1;
    public string HealthLabel = "neznámy";
    public string AlertReason = "";
    public string DisplayText = "S.M.A.R.T: nedostupné";
    public bool ReadTestRan;
    public double ReadTestMbPerSecond;
    public double ReadTestWorstMs;
    public int ReadTestErrors;
    public bool ReadTestTimedOut;
    public QuickReadRating ReadTestRating = QuickReadRating.Unknown;
    public string ReadTestSummary = "";
    public List<SmartAttribute> Attributes = new();
}

public static class SmartInfoService
{
    public static SmartInfo Query(int diskNumber, CancellationToken ct)
    {
        try
        {
            return QueryAsync(diskNumber, ct).GetAwaiter().GetResult();
        }
        catch
        {
            return new SmartInfo { DisplayText = "S.M.A.R.T: nedostupné" };
        }
    }

    public static async Task<SmartInfo> QueryAsync(int diskNumber, CancellationToken ct)
    {
        if (diskNumber < 0)
            return new SmartInfo { DisplayText = "S.M.A.R.T: nedostupné (obraz)" };

        var info = new SmartInfo();

        // NVMe SMART/Health log page dáva rovnaké údaje ako CrystalDiskInfo.
        var nvme = NvmeSmart.Query(diskNumber);
        if (nvme is { Available: true })
        {
            info.Nvme = true;
            info.CriticalWarning = nvme.CriticalWarning;
            info.Temperature = nvme.TemperatureC;
            info.AvailableSpare = nvme.AvailableSpare;
            info.SpareThreshold = nvme.SpareThreshold;
            info.Wear = nvme.PercentageUsed;
            info.PowerCycles = (long)Math.Min(long.MaxValue, nvme.PowerCycles);
            info.PowerOnHours = (long)Math.Min(long.MaxValue, nvme.PowerOnHours);
            info.UnsafeShutdowns = (long)Math.Min(long.MaxValue, nvme.UnsafeShutdowns);
            info.MediaErrors = (long)Math.Min(long.MaxValue, nvme.MediaErrors);
            info.ErrorLogEntries = (long)Math.Min(long.MaxValue, nvme.ErrorLogEntries);
            info.HostReadBytes = nvme.ReadBytes;
            info.HostWriteBytes = nvme.WrittenBytes;
        }

        var script = $$"""
$d = Get-PhysicalDisk -ErrorAction SilentlyContinue | Where-Object { $_.DeviceId -eq {{diskNumber}} } | Select-Object -First 1
if (-not $d) { [pscustomobject]@{ Available = $false } | ConvertTo-Json -Compress; exit }
$r = $d | Get-StorageReliabilityCounter -ErrorAction SilentlyContinue

$modelKey = ([string]$d.FriendlyName -replace '[^A-Za-z0-9]', '').ToUpper()
$pds = @(Get-CimInstance -Namespace root\wmi -ClassName MSStorageDriver_FailurePredictData -ErrorAction SilentlyContinue)
$pd = $pds | Where-Object {
  $k = ([string]$_.InstanceName -replace '[^A-Za-z0-9]', '').ToUpper()
  $modelKey.Length -gt 0 -and $k.Contains($modelKey)
} | Select-Object -First 1
if (-not $pd -and $pds.Count -eq 1 -and $modelKey.Length -eq 0) { $pd = $pds[0] }

$attrs = @()
if ($pd -and $pd.VendorSpecific) {
  $b = $pd.VendorSpecific
  for ($i = 0; $i -lt 30; $i++) {
    $o = 2 + 12 * $i
    if (($o + 11) -ge $b.Length) { break }
    $id = [int]$b[$o]
    if ($id -le 0) { continue }
    $value = [int]$b[$o + 3]
    $worst = [int]$b[$o + 4]
    [int64]$raw = 0
    for ($k = 0; $k -lt 6; $k++) { $raw = $raw -bor ([int64]$b[$o + 5 + $k] -shl (8 * $k)) }
    $attrs += [pscustomobject]@{ Id = $id; Value = $value; Worst = $worst; Raw = $raw }
  }
}

$re = 0; $pe = 0; $uc = 0
foreach ($a in $attrs) {
  if ($a.Id -eq 5) { $re = $a.Raw }
  elseif ($a.Id -eq 197) { $pe = $a.Raw }
  elseif ($a.Id -eq 198) { $uc = $a.Raw }
}

[pscustomobject]@{
  Available = $true
  FriendlyName = [string]$d.FriendlyName
  Health = [string]$d.HealthStatus
  Status = [string]$d.OperationalStatus
  BusType = [string]$d.BusType
  MediaType = [string]$d.MediaType
  PowerOnHours = $r.PowerOnHours
  PowerCycleCount = $r.PowerCycleCount
  Temperature = $r.Temperature
  Wear = $r.Wear
  ReadErrorsTotal = $r.ReadErrorsTotal
  WriteErrorsTotal = $r.WriteErrorsTotal
  ReadErrorsUncorrected = $r.ReadErrorsUncorrected
  WriteErrorsUncorrected = $r.WriteErrorsUncorrected
  ReallocatedSectors = $re
  PendingSectors = $pe
  UncorrectableSectors = $uc
  PredictFailure = $false
  Attributes = $attrs
} | ConvertTo-Json -Compress -Depth 5
""";

        var doc = await Ps.RunJsonAsync(script, ct, 30000).ConfigureAwait(false);
        if (doc is null)
        {
            if (!info.Nvme)
                return new SmartInfo { DisplayText = "S.M.A.R.T: nedostupné" };
            ComposeDisplay(info);
            return info;
        }

        var obj = Ps.Objects(doc).FirstOrDefault();
        if (obj.ValueKind == JsonValueKind.Object && Ps.Bool(obj, "Available"))
        {
            info.Available = true;
            info.Model = Ps.Str(obj, "FriendlyName");
            info.MediaType = Ps.Str(obj, "MediaType");
            info.Interface = Ps.Str(obj, "BusType");
            if (string.IsNullOrWhiteSpace(info.Health)) info.Health = Ps.Str(obj, "Health");
            if (string.IsNullOrWhiteSpace(info.Status)) info.Status = Ps.Str(obj, "Status");
            if (info.Temperature <= 0) info.Temperature = Ps.Long(obj, "Temperature");
            if (info.Wear <= 0) info.Wear = Ps.Long(obj, "Wear");
            if (info.PowerOnHours <= 0) info.PowerOnHours = Ps.Long(obj, "PowerOnHours");
            if (info.PowerCycles <= 0) info.PowerCycles = Ps.Long(obj, "PowerCycleCount");
            if (info.ReadErrorsTotal <= 0) info.ReadErrorsTotal = Ps.Long(obj, "ReadErrorsTotal");
            if (info.WriteErrorsTotal <= 0) info.WriteErrorsTotal = Ps.Long(obj, "WriteErrorsTotal");
            if (info.ReadErrorsUncorrected <= 0) info.ReadErrorsUncorrected = Ps.Long(obj, "ReadErrorsUncorrected");
            if (info.WriteErrorsUncorrected <= 0) info.WriteErrorsUncorrected = Ps.Long(obj, "WriteErrorsUncorrected");
            if (info.ReallocatedSectors <= 0) info.ReallocatedSectors = Ps.Long(obj, "ReallocatedSectors");
            if (info.PendingSectors <= 0) info.PendingSectors = Ps.Long(obj, "PendingSectors");
            if (info.UncorrectableSectors <= 0) info.UncorrectableSectors = Ps.Long(obj, "UncorrectableSectors");
            info.PredictFailure = info.PredictFailure || Ps.Bool(obj, "PredictFailure");

            if (obj.TryGetProperty("Attributes", out var attributes) &&
                attributes.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in attributes.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    info.Attributes.Add(new SmartAttribute
                    {
                        Id = Ps.Int(item, "Id"),
                        Value = Ps.Int(item, "Value"),
                        Worst = Ps.Int(item, "Worst"),
                        Raw = Ps.Long(item, "Raw")
                    });
                }
                ApplyAttributes(info);
            }
        }

        if (!info.Available && !info.Nvme)
            return new SmartInfo { DisplayText = "S.M.A.R.T: nedostupné" };

        info.Available = true;
        ComposeDisplay(info);
        return info;
    }

    private static void ApplyAttributes(SmartInfo info)
    {
        foreach (var attribute in info.Attributes)
        {
            attribute.Name = AttributeName(attribute.Id);
            switch (attribute.Id)
            {
                case 5:
                    if (info.ReallocatedSectors <= 0) info.ReallocatedSectors = attribute.Raw;
                    break;
                case 9:
                    if (info.PowerOnHours <= 0) info.PowerOnHours = attribute.Raw;
                    break;
                case 12:
                    if (info.PowerCycles <= 0) info.PowerCycles = attribute.Raw;
                    break;
                case 173:
                case 177:
                    if (info.Wear <= 0 && attribute.Value is > 0 and < 100)
                        info.Wear = 100 - attribute.Value;
                    break;
                case 174:
                    if (info.UnsafeShutdowns <= 0) info.UnsafeShutdowns = attribute.Raw;
                    break;
                case 187:
                    if (info.UncorrectableSectors <= 0) info.UncorrectableSectors = attribute.Raw;
                    break;
                case 190:
                case 194:
                    if (info.Temperature <= 0 && attribute.Raw > 0)
                        info.Temperature = attribute.Raw & 0xFF;
                    break;
                case 197:
                    if (info.PendingSectors <= 0) info.PendingSectors = attribute.Raw;
                    break;
                case 198:
                    if (info.UncorrectableSectors <= 0) info.UncorrectableSectors = attribute.Raw;
                    break;
                case 199:
                    info.CrcErrors = attribute.Raw;
                    break;
                case 202:
                    if (info.Wear <= 0 && attribute.Raw is > 0 and <= 100) info.Wear = attribute.Raw;
                    break;
                case 231:
                case 233:
                    if (info.RemainingLife < 0 && attribute.Value is > 0 and <= 100)
                        info.RemainingLife = attribute.Value;
                    break;
                case 241:
                    if (!info.Nvme && info.HostWriteBytes == 0 && attribute.Raw > 0)
                        info.HostWriteBytes = (ulong)Math.Min(ulong.MaxValue, (decimal)attribute.Raw * 512);
                    break;
                case 242:
                    if (!info.Nvme && info.HostReadBytes == 0 && attribute.Raw > 0)
                        info.HostReadBytes = (ulong)Math.Min(ulong.MaxValue, (decimal)attribute.Raw * 512);
                    break;
            }
        }
    }

    private static string AttributeName(int id) => id switch
    {
        1 => "Raw Read Error Rate",
        5 => "Reallocated Sectors Count",
        7 => "Seek Error Rate",
        9 => "Power-On Hours",
        10 => "Spin Retry Count",
        12 => "Power Cycle Count",
        173 => "Average Erase Count",
        174 => "Unexpected Power Loss",
        177 => "Wear Leveling Count",
        184 => "End-to-End Error",
        187 => "Reported Uncorrectable",
        190 => "Temperature",
        194 => "Temperature",
        197 => "Current Pending Sector Count",
        198 => "Offline Uncorrectable",
        199 => "UDMA CRC Error Count",
        202 => "Percentage Used",
        231 => "SSD Life Left",
        233 => "Media Wearout Indicator",
        241 => "Total LBAs Written",
        242 => "Total LBAs Read",
        _ => "Attribute " + id
    };

    /// <summary>Zostaví text pre Sprievodcu: všetky dôvody naraz + dni v prevádzke.</summary>
    public static void ComposeDisplay(SmartInfo info)
    {
        info.PowerOnDays = info.PowerOnHours / 24;

        var reasons = new List<string>();
        bool replacement = false;

        if (info.PredictFailure) { reasons.Add("predikcia zlyhania"); replacement = true; }
        if (info.CriticalWarning != 0)
        {
            reasons.Add("NVMe varovanie 0x" + info.CriticalWarning.ToString("X2"));
            replacement = true;
        }
        if (!string.IsNullOrWhiteSpace(info.Health) &&
            !info.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("stav " + info.Health);
            replacement = true;
        }
        if (!string.IsNullOrWhiteSpace(info.Status) &&
            !info.Status.Equals("OK", StringComparison.OrdinalIgnoreCase) &&
            !info.Status.Equals("Online", StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("stav " + info.Status);
            replacement = true;
        }
        if (info.Temperature > (info.Nvme ? 70 : 55))
            reasons.Add($"vysoká teplota {info.Temperature} °C");
        if (info.Wear >= 80) { reasons.Add($"opotrebenie {info.Wear} %"); replacement = true; }
        if (info.PowerOnHours >= 30_000)
        {
            reasons.Add($"veľmi vysoký počet hodín ({info.PowerOnDays:N0} dní) – zvážte výmenu");
            replacement = true;
        }
        else if (info.PowerOnHours >= 15_000)
        {
            reasons.Add($"vysoký počet hodín ({info.PowerOnDays:N0} dní) – zvážte výmenu");
        }

        if (info.ReadTestRating == QuickReadRating.Bad)
        {
            reasons.Add($"pomalé čítanie {info.ReadTestMbPerSecond:0.0} MB/s – plná kontrola povrchu");
            if (info.ReadTestErrors > 0 || info.ReadTestTimedOut) replacement = true;
        }
        else if (info.ReadTestRating == QuickReadRating.Caution)
        {
            reasons.Add($"čítanie {info.ReadTestMbPerSecond:0.0} MB/s – odporúčaná kontrola povrchu");
        }

        if (info.ReallocatedSectors > 0)
        {
            reasons.Add($"vadné sektory (presunuté): {info.ReallocatedSectors}");
            replacement = true;
        }
        if (info.PendingSectors > 0)
        {
            reasons.Add($"nestabilné sektory: {info.PendingSectors}");
            replacement = true;
        }
        if (info.UncorrectableSectors > 0)
        {
            reasons.Add($"neopraviteľné sektory: {info.UncorrectableSectors}");
            replacement = true;
        }
        if (info.MediaErrors > 0)
        {
            reasons.Add($"chyby média: {info.MediaErrors}");
            replacement = true;
        }
        if (info.AvailableSpare >= 0 && info.SpareThreshold > 0 &&
            info.AvailableSpare < info.SpareThreshold)
        {
            reasons.Add($"náhradné bloky pod prahom ({info.AvailableSpare} % < {info.SpareThreshold} %)");
            replacement = true;
        }
        if (info.ReadErrorsUncorrected > 0)
        {
            reasons.Add($"neopravené chyby čítania: {info.ReadErrorsUncorrected}");
            replacement = true;
        }
        else if (info.ReadErrorsTotal > 0)
        {
            reasons.Add($"chyby čítania: {info.ReadErrorsTotal}");
        }
        if (info.WriteErrorsUncorrected > 0)
        {
            reasons.Add($"neopravené chyby zápisu: {info.WriteErrorsUncorrected}");
            replacement = true;
        }
        else if (info.WriteErrorsTotal > 0)
        {
            reasons.Add($"chyby zápisu: {info.WriteErrorsTotal}");
        }

        ComputeHealth(info);

        var uptime = info.PowerOnHours > 0
            ? $"{info.PowerOnDays:N0} dní v prevádzke"
            : "dni v prevádzke nie sú dostupné";

        info.HasAlert = reasons.Count > 0;
        info.ReplacementRecommended = replacement || info.Rating == SmartRating.Bad;
        if (info.HasAlert)
        {
            info.AlertReason = string.Join("; ", reasons);
            info.DisplayText = "S.M.A.R.T: ALERT – " + info.AlertReason +
                               " · " + uptime +
                               (info.ReplacementRecommended
                                   ? " · Odporúčaná výmena disku"
                                   : " · Zálohujte dáta");
        }
        else
        {
            info.DisplayText = $"S.M.A.R.T: {info.HealthLabel}" +
                               (info.HealthScore >= 0 ? $" {info.HealthScore} %" : "") +
                               $" · {uptime}";
        }
    }

    private static void ComputeHealth(SmartInfo info)
    {
        if (!info.Available)
        {
            info.Rating = SmartRating.Unknown;
            info.HealthScore = -1;
            info.HealthLabel = "nedostupné";
            return;
        }

        int score = 100;

        if (info.Nvme || info.Wear > 0)
        {
            info.RemainingLife = Math.Clamp(100 - info.Wear, 0, 100);
            score = Math.Min(score, (int)info.RemainingLife);
        }
        else if (info.RemainingLife >= 0)
        {
            score = Math.Min(score, (int)info.RemainingLife);
        }

        if (info.ReallocatedSectors > 0) score -= 10;
        if (info.PendingSectors > 0) score -= 25;
        if (info.UncorrectableSectors > 0) score -= 25;
        if (info.MediaErrors > 0) score -= 20;
        if (info.ReadErrorsUncorrected > 0) score -= 10;
        if (info.WriteErrorsUncorrected > 0) score -= 10;
        if (info.CrcErrors > 0) score -= 3;
        if (info.CriticalWarning != 0) score -= 30;
        if (!string.IsNullOrWhiteSpace(info.Health) &&
            !info.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase)) score -= 30;
        if (info.PredictFailure) score -= 40;
        if (info.PowerOnHours >= 30_000) score -= 20;
        else if (info.PowerOnHours >= 15_000) score -= 10;

        if (info.ReadTestRating == QuickReadRating.Bad) score -= 25;
        else if (info.ReadTestRating == QuickReadRating.Caution) score -= 10;
        if (info.ReadTestErrors > 0 || info.ReadTestTimedOut) score -= 10;

        if (info.Nvme)
        {
            if (info.Temperature > 75) score -= 10;
            else if (info.Temperature > 70) score -= 5;
        }
        else if (info.Temperature > 60) score -= 5;

        info.HealthScore = Math.Clamp(score, 0, 100);
        info.Rating = info.HealthScore switch
        {
            >= 85 => SmartRating.Good,
            >= 60 => SmartRating.Caution,
            _ => SmartRating.Bad
        };
        info.HealthLabel = info.HealthScore switch
        {
            >= 95 => "Výborný",
            >= 85 => "Dobrý",
            >= 60 => "Pozor",
            _ => "Kritický"
        };
    }
}

