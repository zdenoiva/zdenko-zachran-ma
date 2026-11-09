using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NtfsRescue.Core;
using NtfsRescue.Util;

namespace NtfsRescue.Repair;

public sealed class BackupResult
{
    public string Folder = "";
    public readonly List<string> Files = new();
    public long Bytes;
}

public static class MetadataBackup
{
    private const int BlockSize = 1 << 20;

    public static BackupResult Create(
        NtfsVolume volume,
        char letter,
        string baseFolder,
        NtfsScanResult? inventory,
        Action<string> log,
        IProgress<string>? phase,
        CancellationToken ct)
    {
        var result = new BackupResult();
        baseFolder = Path.GetFullPath(baseFolder);

        int destDisk = VolumeMapper.GetDiskNumberForPath(baseFolder);
        if (destDisk >= 0 && destDisk == volume.Disk.DiskNumber)
            throw new IOException("Priečinok pre zálohu je na tom istom fyzickom disku ako zdroj. Vyberte iný disk.");

        Directory.CreateDirectory(baseFolder);

        long mftLength = Math.Min(volume.MftRealSize, 512L * 1024 * 1024);
        long expected = (2L * BlockSize) + mftLength + (inventory is null ? 0 : 64L * 1024 * 1024);
        EnsureFreeSpace(baseFolder, expected);

        if (volume.MftRealSize > mftLength)
            log($"Pozor: $MFT má {Fmt.Bytes(volume.MftRealSize)}; zálohujem prvých {Fmt.Bytes(mftLength)}.");

        var folder = Path.Combine(baseFolder, $"backup-{letter}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(folder);
        result.Folder = folder;

        log($"Záloha metadát do: {folder}");

        phase?.Report("Záloha boot sectoru");
        int bootSize = (int)Math.Min(BlockSize, Math.Max(512, volume.PartitionLength));
        var primary = volume.Disk.ReadBytes(volume.PartitionOffset, bootSize);
        result.Bytes += WriteFile(Path.Combine(folder, "boot-primary.bin"), primary, ct);
        result.Files.Add("boot-primary.bin");

        if (volume.PartitionLength > 512)
        {
            var backup = volume.Disk.ReadBytes(volume.PartitionOffset + volume.PartitionLength - bootSize, bootSize);
            result.Bytes += WriteFile(Path.Combine(folder, "boot-backup.bin"), backup, ct);
            result.Files.Add("boot-backup.bin");
        }

        if (mftLength > 0)
        {
            phase?.Report("Záloha $MFT");
            log($"Kopírujem $MFT ({Fmt.Bytes(mftLength)})...");
            using var output = new FileStream(Path.Combine(folder, "mft.bin"), FileMode.Create,
                FileAccess.Write, FileShare.None, BlockSize, FileOptions.SequentialScan);

            var buffer = new byte[BlockSize];
            long offset = 0;
            while (offset < mftLength)
            {
                ct.ThrowIfCancellationRequested();
                int chunk = (int)Math.Min(BlockSize, mftLength - offset);
                volume.ReadStreamBytes(volume.MftRuns, offset, buffer, 0, chunk);
                output.Write(buffer, 0, chunk);
                offset += chunk;
                if ((offset & ((64L * 1024 * 1024) - 1)) == 0)
                    log($"  $MFT: {Fmt.Bytes(offset)} / {Fmt.Bytes(mftLength)}");
            }

            output.Flush();
            result.Bytes += mftLength;
            result.Files.Add("mft.bin");
        }

        phase?.Report("Zapisujem informácie");
        var info = new
        {
            Created = DateTime.Now,
            Drive = $"{letter}:",
            VolumeLabel = (string?)null,
            volume.Boot.BytesPerSector,
            volume.Boot.SectorsPerCluster,
            volume.Boot.ClusterSize,
            volume.Boot.VolumeSize,
            volume.Boot.MftLcn,
            volume.Boot.MftMirrLcn,
            volume.Boot.MftRecordSize,
            volume.Boot.Serial,
            volume.BootFromBackup,
            volume.TotalRecords,
            volume.MftRealSize,
            Disk = new
            {
                volume.Disk.DiskNumber,
                volume.Disk.SectorSize,
                volume.Disk.Size,
                volume.Disk.DevicePath
            },
            PartitionOffset = volume.PartitionOffset,
            PartitionLength = volume.PartitionLength
        };

        var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
        result.Bytes += WriteFile(Path.Combine(folder, "volume.json"), Encoding.UTF8.GetBytes(json), ct);
        result.Files.Add("volume.json");

        if (inventory is not null)
        {
            phase?.Report("Inventár súborov");
            var csvPath = Path.Combine(folder, "inventar.csv");
            CsvInventory.WriteAsync(inventory.Entries, csvPath, ct).GetAwaiter().GetResult();
            result.Files.Add("inventar.csv");
            result.Bytes += new FileInfo(csvPath).Length;
            log($"Inventár: {inventory.Entries.Count:N0} záznamov");
        }

        phase?.Report("Kontrolné súčty");
        var checksums = new StringBuilder();
        foreach (var name in result.Files)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(folder, name);
            checksums.AppendLine($"{Sha256(path)}  {name}");
        }
        result.Bytes += WriteFile(Path.Combine(folder, "checksums.sha256"),
            Encoding.UTF8.GetBytes(checksums.ToString()), ct);
        result.Files.Add("checksums.sha256");

        log($"Záloha hotová: {Fmt.Bytes(result.Bytes)}");
        return result;
    }

    private static long WriteFile(string path, byte[] data, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        File.WriteAllBytes(path, data);
        return data.Length;
    }

    private static void EnsureFreeSpace(string path, long needed)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root)) return;
            var di = new DriveInfo(root);
            if (!di.IsReady) return;

            long reserve = 128L * 1024 * 1024;
            if (di.AvailableFreeSpace < needed + reserve)
                throw new IOException(
                    $"Na disku {root} nie je dosť miesta. Potrebné aspoň {Fmt.Bytes(needed + reserve)}, " +
                    $"voľné {Fmt.Bytes(di.AvailableFreeSpace)}.");
        }
        catch (IOException) { throw; }
        catch { }
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }
}
