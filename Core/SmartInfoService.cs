using NtfsRescue.Util;

namespace NtfsRescue.Core;

public sealed class SmartInfo
{
    public bool Available;
    public string Model = "";
    public string Health = "";
    public string Status = "";
    public long PowerOnHours;
    public long PowerOnDays;
    public long Temperature;
    public long Wear;
    public long ReadErrorsTotal;
    public long WriteErrorsTotal;
    public long ReadErrorsUncorrected;
    public long WriteErrorsUncorrected;
    public long ReallocatedSectors;
    public long PendingSectors;
    public long UncorrectableSectors;
    public bool PredictFailure;
    public bool HasAlert;
    public bool ReplacementRecommended;
    public string AlertReason = "";
    public string DisplayText = "S.M.A.R.T: nedostupné";
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
if (-not $pd -and $pds.Count -eq 1) { $pd = $pds[0] }

$re = 0; $pe = 0; $uc = 0
if ($pd -and $pd.VendorSpecific) {
  $b = $pd.VendorSpecific
  for ($i = 0; $i -lt 30; $i++) {
    $o = 2 + 12 * $i
    if (($o + 11) -ge $b.Length) { break }
    $id = [int]$b[$o]
    $raw = [uint64]$b[$o+5] -bor ([uint64]$b[$o+6] -shl 8) -bor ([uint64]$b[$o+7] -shl 16) -bor ([uint64]$b[$o+8] -shl 24) -bor ([uint64]$b[$o+9] -shl 32) -bor ([uint64]$b[$o+10] -shl 40)
    $raw = $raw -band 0xFFFFFFFF
    if ($id -eq 5) { $re = $raw }
    elseif ($id -eq 197) { $pe = $raw }
    elseif ($id -eq 198) { $uc = $raw }
  }
}

[pscustomobject]@{
  Available = $true
  FriendlyName = [string]$d.FriendlyName
  Health = [string]$d.HealthStatus
  Status = [string]$d.OperationalStatus
  PowerOnHours = $r.PowerOnHours
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
} | ConvertTo-Json -Compress
""";

        var doc = await Ps.RunJsonAsync(script, ct, 30000).ConfigureAwait(false);
        if (doc is null)
            return new SmartInfo { DisplayText = "S.M.A.R.T: nedostupné" };

        var obj = Ps.Objects(doc).FirstOrDefault();
        if (obj.ValueKind != System.Text.Json.JsonValueKind.Object)
            return new SmartInfo { DisplayText = "S.M.A.R.T: nedostupné" };

        if (!Ps.Bool(obj, "Available"))
            return new SmartInfo { DisplayText = "S.M.A.R.T: nedostupné" };

        var info = new SmartInfo
        {
            Available = true,
            Model = Ps.Str(obj, "FriendlyName"),
            Health = Ps.Str(obj, "Health"),
            Status = Ps.Str(obj, "Status"),
            PowerOnHours = Ps.Long(obj, "PowerOnHours"),
            Temperature = Ps.Long(obj, "Temperature"),
            Wear = Ps.Long(obj, "Wear"),
            ReadErrorsTotal = Ps.Long(obj, "ReadErrorsTotal"),
            WriteErrorsTotal = Ps.Long(obj, "WriteErrorsTotal"),
            ReadErrorsUncorrected = Ps.Long(obj, "ReadErrorsUncorrected"),
            WriteErrorsUncorrected = Ps.Long(obj, "WriteErrorsUncorrected"),
            ReallocatedSectors = Ps.Long(obj, "ReallocatedSectors"),
            PendingSectors = Ps.Long(obj, "PendingSectors"),
            UncorrectableSectors = Ps.Long(obj, "UncorrectableSectors"),
            PredictFailure = Ps.Bool(obj, "PredictFailure")
        };
        ComposeDisplay(info);
        return info;
    }

    /// <summary>Zostaví text pre Sprievodcu: všetky dôvody naraz + dni v prevádzke.</summary>
    public static void ComposeDisplay(SmartInfo info)
    {
        info.PowerOnDays = info.PowerOnHours / 24;

        var reasons = new List<string>();
        bool replacement = false;

        if (info.PredictFailure) { reasons.Add("predikcia zlyhania"); replacement = true; }
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
        if (info.Temperature > 55) reasons.Add($"vysoká teplota {info.Temperature} °C");
        if (info.Wear >= 80) { reasons.Add($"opotrebenie {info.Wear} %"); replacement = true; }

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

        var uptime = info.PowerOnHours > 0
            ? $"{info.PowerOnDays:N0} dní v prevádzke"
            : "dni v prevádzke nie sú dostupné";

        info.HasAlert = reasons.Count > 0;
        info.ReplacementRecommended = replacement;
        if (info.HasAlert)
        {
            info.AlertReason = string.Join("; ", reasons);
            info.DisplayText = "S.M.A.R.T: ALERT – " + info.AlertReason +
                               " · " + uptime +
                               (replacement
                                   ? " · Odporúčaná výmena disku"
                                   : " · Zálohujte dáta");
        }
        else
        {
            info.DisplayText = $"S.M.A.R.T: OK · {uptime}";
        }
    }
}
