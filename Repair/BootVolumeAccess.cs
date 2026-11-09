using NtfsRescue.Core;
using NtfsRescue.Util;

namespace NtfsRescue.Repair;

public static class BootVolumeAccess
{
    /// <summary>Dočasne priradí písmeno partícii (aj ESP/recovery) a vráti ho.</summary>
    public static async Task<char?> EnsureLetterAsync(DiskPartition partition, Action<string>? log,
        CancellationToken ct)
    {
        if (partition.HasLetter) return partition.Letter;

        var letter = await PartitionAccess.AssignDriveLetterAsync(partition, ct).ConfigureAwait(false);
        if (letter.HasValue) return letter;

        try
        {
            var script =
                $"$p = Get-Partition -DiskNumber {partition.DiskNumber} -PartitionNumber {partition.PartitionNumber} -ErrorAction Stop; " +
                "$v = $p | Get-Volume -ErrorAction SilentlyContinue; " +
                "if ($v -and $v.UniqueId) { [string]$v.UniqueId }";
            var volumeId = await Ps.RunAsync(script, ct, 20000).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(volumeId) || !volumeId.Contains("Volume", StringComparison.OrdinalIgnoreCase))
                return null;

            var free = FindFreeLetter();
            if (free is null) return null;

            var result = await ProcessRunner.RunAsync(
                Path.Combine(Environment.SystemDirectory, "mountvol.exe"),
                new[] { $"{free}:", volumeId.Trim() }, log, ct, 30000).ConfigureAwait(false);
            if (result.ExitCode != 0) return null;

            partition.DriveLetter = free.ToString()!;
            log?.Invoke($"Partícii {partition.PartitionNumber} bolo dočasne priradené {free}:");
            return free;
        }
        catch (Exception ex)
        {
            log?.Invoke("Nepodarilo sa namontovať partíciu: " + ex.Message);
            return null;
        }
    }

    public static async Task ReleaseAsync(DiskPartition partition, bool wasMountedByUs,
        CancellationToken ct)
    {
        if (!wasMountedByUs || !partition.HasLetter) return;
        try { await PartitionAccess.RemoveDriveLetterAsync(partition, ct).ConfigureAwait(false); }
        catch { }
    }

    private static char? FindFreeLetter()
    {
        var used = new HashSet<char>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!string.IsNullOrEmpty(drive.Name) && drive.Name.Length >= 1)
                    used.Add(char.ToUpperInvariant(drive.Name[0]));
            }
        }
        catch { }

        for (char c = 'Z'; c >= 'D'; c--)
            if (!used.Contains(c)) return c;
        return null;
    }
}
