using NtfsRescue.Util;

namespace NtfsRescue.Core;

/// <summary>
/// Reset USB mostíka cez PnP. Niektoré boxy (napr. ASM1352-PM) po nečinnosti
/// vypnú SATA port a potom hlásia "device not ready", kým sa USB zariadenie
/// znovu nenájde. Reset je bezpečný: nerobí žiadny zápis na disk.
/// </summary>
public static class UsbBridge
{
    /// <summary>Automatický reset pri "read 21" (dá sa vypnúť ZDENKO_NO_USBRESET=1).</summary>
    public static bool AutoReset =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ZDENKO_NO_USBRESET"));

    public static bool Reset(int diskNumber)
        => ResetAsync(diskNumber, CancellationToken.None).GetAwaiter().GetResult();

    public static async Task<bool> ResetAsync(int diskNumber, CancellationToken ct)
    {
        var script = $$"""
$id = [string](Get-CimInstance Win32_DiskDrive -ErrorAction SilentlyContinue |
  Where-Object { $_.Index -eq {{diskNumber}} } | Select-Object -First 1).PNPDeviceID
for ($i = 0; $i -lt 8 -and $id; $i++) {
  if ($id -like 'USB\VID_*') { break }
  $id = [string](Get-PnpDeviceProperty -InstanceId $id -KeyName 'DEVPKEY_Device_Parent' -ErrorAction SilentlyContinue).Data
}
if ($id -like 'USB\VID_*') {
  & pnputil /restart-device $id 2>&1 | Out-Null
  if ($LASTEXITCODE -eq 0) { 'RESET-OK' } else { 'RESET-FAIL' }
} else { 'NO-USB-PARENT' }
""";
        try
        {
            var text = await Ps.RunAsync(script, ct, 20000).ConfigureAwait(false);
            bool ok = text?.Contains("RESET-OK", StringComparison.Ordinal) == true;
            if (ok) Log.Info($"USB mostík pre disk {diskNumber}: reset OK");
            else Log.Warn($"USB mostík pre disk {diskNumber}: reset zlyhal ({text?.Trim()})");
            return ok;
        }
        catch (Exception ex)
        {
            Log.Warn($"USB mostík pre disk {diskNumber}: reset chyba {ex.Message}");
            return false;
        }
    }
}
