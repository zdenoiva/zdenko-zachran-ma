using NtfsRescue.Core;
using NtfsRescue.Util;

namespace NtfsRescue.Repair;

public static class PartitionAccess
{
    public static async Task<char?> AssignDriveLetterAsync(DiskPartition partition, CancellationToken ct)
    {
        if (partition.HasLetter) return partition.Letter;
        if (partition.PartitionNumber <= 0) return null;

        var script =
            $"$p = Get-Partition -DiskNumber {partition.DiskNumber} -PartitionNumber {partition.PartitionNumber} -ErrorAction Stop; " +
            "if (-not $p.DriveLetter) { $p | Add-PartitionAccessPath -AssignDriveLetter -ErrorAction Stop; Start-Sleep -Milliseconds 600 }; " +
            $"$l = (Get-Partition -DiskNumber {partition.DiskNumber} -PartitionNumber {partition.PartitionNumber}).DriveLetter; " +
            "if ($l) { [string]$l }";

        var output = await Ps.RunAsync(script, ct, 30000).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(output)) return null;

        foreach (char c in output.Trim().Reverse())
        {
            if (c is >= 'A' and <= 'Z')
            {
                partition.DriveLetter = c.ToString();
                Log.Info($"Partícii Disk {partition.DiskNumber}/{partition.PartitionNumber} bolo priradené {c}:");
                return c;
            }
        }

        return null;
    }

    public static async Task RemoveDriveLetterAsync(DiskPartition partition, CancellationToken ct)
    {
        if (!partition.HasLetter || partition.PartitionNumber <= 0) return;
        char letter = partition.Letter;

        var script =
            $"$p = Get-Partition -DiskNumber {partition.DiskNumber} -PartitionNumber {partition.PartitionNumber} -ErrorAction Stop; " +
            "if ($p.DriveLetter) { $path = \"$($p.DriveLetter):\\\"; " +
            $"Remove-PartitionAccessPath -DiskNumber {partition.DiskNumber} -PartitionNumber {partition.PartitionNumber} -AccessPath $path -ErrorAction Stop }}";

        await Ps.RunAsync(script, ct, 30000).ConfigureAwait(false);
        partition.DriveLetter = "";
        Log.Info($"Dočasné písmeno {letter}: bolo odobrané.");
    }
}
