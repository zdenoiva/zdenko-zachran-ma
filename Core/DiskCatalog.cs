using System.Text;
using NtfsRescue.Util;

namespace NtfsRescue.Core;

public static class DiskCatalog
{
    public static Task<List<DiskPartition>> LoadAsync(CancellationToken ct)
        => LoadCoreAsync(ct, includeHidden: false);

    /// <summary>Vrátane systémových, recovery a skrytých partícií (pre boot analýzu).</summary>
    public static Task<List<DiskPartition>> LoadAllAsync(CancellationToken ct)
        => LoadCoreAsync(ct, includeHidden: true);

    private static async Task<List<DiskPartition>> LoadCoreAsync(CancellationToken ct, bool includeHidden)
    {
        var result = new List<DiskPartition>();

        var diskDoc = await Ps.RunJsonAsync(
            "Get-Disk -ErrorAction SilentlyContinue | Select-Object Number,FriendlyName,SerialNumber,Size,BusType,PartitionStyle,LogicalSectorSize,Signature,Guid,IsBoot,IsSystem,IsOffline,IsReadOnly | ConvertTo-Json -Compress",
            ct, 30000).ConfigureAwait(false);

        var partDoc = await Ps.RunJsonAsync(
            "Get-Partition -ErrorAction SilentlyContinue | Select-Object DiskNumber,PartitionNumber,DriveLetter,Offset,Size,Type,GptType,MbrType,IsActive,IsHidden,IsOffline,IsReadOnly | ConvertTo-Json -Compress",
            ct, 30000).ConfigureAwait(false);

        if (diskDoc is null || partDoc is null)
            return await FallbackByVolumesAsync(ct).ConfigureAwait(false);

        var disks = new Dictionary<int, (string Model, string Serial, long Size, int Sector,
            string Style, string Guid, string Signature, bool IsBoot, bool IsSystem,
            bool IsOffline, bool IsReadOnly, string BusType)>();
        foreach (var d in Ps.Objects(diskDoc))
        {
            int number = Ps.Int(d, "Number");
            if (number < 0) continue;
            int sector = Ps.Int(d, "LogicalSectorSize");
            if (sector is not (512 or 1024 or 2048 or 4096)) sector = 512;
            disks[number] = (Ps.Str(d, "FriendlyName"), Ps.Str(d, "SerialNumber"),
                Ps.Long(d, "Size"), sector, Ps.Str(d, "PartitionStyle"),
                Ps.Str(d, "Guid"), Ps.Str(d, "Signature"), Ps.Bool(d, "IsBoot"),
                Ps.Bool(d, "IsSystem"), Ps.Bool(d, "IsOffline"), Ps.Bool(d, "IsReadOnly"),
                Ps.Str(d, "BusType"));
        }

        var byDisk = new Dictionary<int, List<DiskPartition>>();
        foreach (var p in Ps.Objects(partDoc))
        {
            int diskNumber = Ps.Int(p, "DiskNumber");
            int partNumber = Ps.Int(p, "PartitionNumber");
            long offset = Ps.Long(p, "Offset");
            long size = Ps.Long(p, "Size");
            if (diskNumber < 0 || partNumber <= 0 || size <= 0) continue;

            if (!disks.TryGetValue(diskNumber, out var disk))
                disk = ("", "", 0, 512, "", "", "", false, false, false, false, "");

            var dp = new DiskPartition
            {
                DiskNumber = diskNumber,
                PartitionNumber = partNumber,
                Offset = offset,
                Length = size,
                SectorSize = disk.Sector,
                DiskSize = disk.Size,
                Model = disk.Model,
                Serial = disk.Serial,
                PartitionType = Ps.Str(p, "Type"),
                GptType = Ps.Str(p, "GptType"),
                MbrType = Ps.Str(p, "MbrType"),
                DriveLetter = NormalizeLetter(Ps.Str(p, "DriveLetter")),
                IsGpt = !string.IsNullOrWhiteSpace(Ps.Str(p, "GptType")),
                IsActive = Ps.Bool(p, "IsActive"),
                IsHidden = Ps.Bool(p, "IsHidden"),
                IsOffline = Ps.Bool(p, "IsOffline"),
                IsReadOnly = Ps.Bool(p, "IsReadOnly"),
                PartitionStyle = disk.Style,
                DiskGuid = disk.Guid,
                DiskSignature = disk.Signature,
                DiskIsBoot = disk.IsBoot,
                DiskIsSystem = disk.IsSystem,
                DiskIsOffline = disk.IsOffline,
                DiskIsReadOnly = disk.IsReadOnly,
                BusType = disk.BusType
            };

            var type = dp.PartitionType;
            if (!includeHidden &&
                (type.Contains("Reserved", StringComparison.OrdinalIgnoreCase) ||
                 type.Contains("Recovery", StringComparison.OrdinalIgnoreCase) ||
                 type.Contains("System", StringComparison.OrdinalIgnoreCase)))
            {
                // Systémové/recovery partície nezobrazujeme v bežnom zozname.
                continue;
            }

            if (dp.HasLetter)
            {
                try
                {
                    var di = new DriveInfo($"{dp.DriveLetter}:");
                    if (di.IsReady) dp.Label = di.VolumeLabel;
                }
                catch { }
            }

            var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            dp.IsSystem |= dp.HasLetter &&
                          systemRoot.StartsWith($"{dp.DriveLetter}:", StringComparison.OrdinalIgnoreCase);

            if (!byDisk.TryGetValue(diskNumber, out var list))
                byDisk[diskNumber] = list = new List<DiskPartition>();
            list.Add(dp);
        }

        foreach (var (diskNumber, list) in byDisk)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var disk = new RawDisk(diskNumber, list[0].SectorSize, list[0].DiskSize);
                foreach (var p in list)
                {
                    p.FileSystem = DetectFileSystem(disk, p);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Disk {diskNumber}: nepodarilo sa prečítať boot sectory: {ex.Message}");
                foreach (var p in list)
                    p.FileSystem = p.FileSystem == FileSystemKind.Unknown ? FileSystemKind.Raw : p.FileSystem;
            }
        }

        foreach (var list in byDisk.Values) result.AddRange(list);
        result.Sort((a, b) => a.DiskNumber != b.DiskNumber
            ? a.DiskNumber.CompareTo(b.DiskNumber)
            : a.Offset.CompareTo(b.Offset));

        if (result.Count == 0)
            return await FallbackByVolumesAsync(ct).ConfigureAwait(false);

        return result;
    }

    private static async Task<List<DiskPartition>> FallbackByVolumesAsync(CancellationToken ct)
    {
        var result = new List<DiskPartition>();
        var volumes = await VolumeCatalog.LoadAsync(ct).ConfigureAwait(false);
        foreach (var v in volumes)
        {
            var loc = VolumeMapper.Locate(v.Letter);
            if (!loc.IsValid) continue;
            result.Add(new DiskPartition
            {
                DiskNumber = loc.DiskNumber,
                PartitionNumber = 0,
                Offset = loc.Offset,
                Length = loc.Length,
                SectorSize = loc.SectorSize,
                DiskSize = loc.DiskSize,
                Model = loc.Model,
                Serial = loc.Serial,
                DriveLetter = v.Letter.ToString(),
                Label = v.Label,
                FileSystem = v.FileSystem.Contains("NTFS", StringComparison.OrdinalIgnoreCase)
                    ? FileSystemKind.Ntfs
                    : v.FileSystem.Contains("FAT32", StringComparison.OrdinalIgnoreCase)
                        ? FileSystemKind.Fat32
                        : FileSystemKind.Unknown
            });
        }
        result.Sort((a, b) => a.DiskNumber.CompareTo(b.DiskNumber));
        return result;
    }

    public static FileSystemKind DetectFileSystem(RawDisk disk, DiskPartition partition)
    {
        try
        {
            var boot = disk.ReadBytes(partition.Offset, 512);
            var fs = DetectFileSystem(boot);
            if (fs is FileSystemKind.Ntfs or FileSystemKind.Fat32) return fs;

            // Poškodený primárny boot sector? Skús záložný FAT32 boot (sektor 6).
            try
            {
                var backup = disk.ReadBytes(partition.Offset + 6L * partition.SectorSize, 512);
                if (Fat32BootSector.Parse(backup).IsValid) return FileSystemKind.Fat32;
            }
            catch { }

            // Alebo záložný NTFS boot sector na konci partície.
            if (partition.Length > partition.SectorSize)
            {
                try
                {
                    var backup = disk.ReadBytes(partition.Offset + partition.Length - partition.SectorSize, 512);
                    if (NtfsBootSector.Parse(backup).IsValid) return FileSystemKind.Ntfs;
                }
                catch { }
            }

            return fs;
        }
        catch
        {
            return FileSystemKind.Unknown;
        }
    }

    public static FileSystemKind DetectFileSystem(byte[] boot)
    {
        if (boot.Length < 512) return FileSystemKind.Unknown;

        if (boot[3] == (byte)'N' && boot[4] == (byte)'T' && boot[5] == (byte)'F' &&
            boot[6] == (byte)'S')
        {
            var ntfs = NtfsBootSector.Parse(boot);
            return ntfs.IsValid ? FileSystemKind.Ntfs : FileSystemKind.Unknown;
        }

        if (boot[3] == (byte)'E' && boot[4] == (byte)'X' && boot[5] == (byte)'F' &&
            boot[6] == (byte)'A' && boot[7] == (byte)'T')
            return FileSystemKind.ExFat;

        var fat = Fat32BootSector.Parse(boot);
        if (fat.IsValid) return FileSystemKind.Fat32;

        if (boot[510] == 0x55 && boot[511] == 0xAA)
            return FileSystemKind.Raw;

        return FileSystemKind.Unknown;
    }

    public static async Task<List<DiskPartition>> LoadImageAsync(string imagePath, CancellationToken ct)
    {
        return await Task.Run(() => LoadImage(imagePath, ct), ct).ConfigureAwait(false);
    }

    public static List<DiskPartition> LoadImage(string imagePath, CancellationToken ct)
    {
        var result = new List<DiskPartition>();
        using var disk = RawDisk.OpenFile(imagePath);
        long fileLength = disk.Size;
        var first = disk.ReadBytes(0, 512);

        var rawParts = new List<(long Offset, long Length, string Name, string Type)>();

        // Ak je to obraz partície (začína boot sectorom), berieme ho ako jeden celok.
        var fs0 = DetectFileSystem(first);
        if (fs0 is FileSystemKind.Ntfs or FileSystemKind.Fat32 or FileSystemKind.ExFat)
        {
            rawParts.Add((0, fileLength, "Obraz partície", fs0.ToSlovak()));
        }

        // GPT cez protective MBR
        bool protective = rawParts.Count == 0 && first.Length >= 512 && first[0x1BE + 4] == 0xEE;
        if (protective)
        {
            try
            {
                var gpt = disk.ReadBytes(512, 512);
                if (Encoding.ASCII.GetString(gpt, 0, 8) == "EFI PART")
                {
                    long entryLba = (long)BitConverter.ToUInt64(gpt, 72);
                    uint count = BitConverter.ToUInt32(gpt, 80);
                    uint entrySize = BitConverter.ToUInt32(gpt, 84);
                    if (count > 0 && count < 512 && entrySize >= 128 && entrySize <= 4096)
                    {
                        var table = disk.ReadBytes(entryLba * 512, (int)(count * entrySize));
                        for (int i = 0; i < count; i++)
                        {
                            int off = (int)(i * entrySize);
                            bool empty = true;
                            for (int b = 0; b < 16; b++) if (table[off + b] != 0) { empty = false; break; }
                            if (empty) continue;

                            long firstLba = BitConverter.ToInt64(table, off + 40);
                            long lastLba = BitConverter.ToInt64(table, off + 48);
                            if (lastLba < firstLba) continue;
                            var name = Encoding.Unicode.GetString(table, off + 56, 72).TrimEnd('\0');
                            rawParts.Add((firstLba * 512, (lastLba - firstLba + 1) * 512,
                                string.IsNullOrWhiteSpace(name) ? $"Partícia {i + 1}" : name, "GPT"));
                        }
                    }
                }
            }
            catch { }
        }

        if (rawParts.Count == 0 && first.Length >= 512 && first[510] == 0x55 && first[511] == 0xAA)
        {
            for (int i = 0; i < 4; i++)
            {
                int off = 0x1BE + i * 16;
                byte type = first[off + 4];
                if (type == 0 || type == 0xEE) continue;
                long start = BitConverter.ToUInt32(first, off + 8);
                long sectors = BitConverter.ToUInt32(first, off + 12);
                if (start <= 0 || sectors <= 0) continue;
                rawParts.Add((start * 512, sectors * 512, $"Partícia {i + 1}", type.ToString("X2")));
            }
        }

        if (rawParts.Count == 0)
            rawParts.Add((0, fileLength, "Celý obraz", "RAW"));

        int index = 1;
        foreach (var part in rawParts)
        {
            ct.ThrowIfCancellationRequested();
            long offset = part.Offset;
            long length = part.Length;
            string name = part.Name;
            string type = part.Type;
            if (offset < 0 || length <= 0 || offset >= fileLength) continue;
            length = Math.Min(length, fileLength - offset);

            FileSystemKind fs = FileSystemKind.Unknown;
            try
            {
                var boot = disk.ReadBytes(offset, 512);
                fs = DetectFileSystem(boot);
            }
            catch { }

            result.Add(new DiskPartition
            {
                DiskNumber = -1,
                PartitionNumber = index++,
                Offset = offset,
                Length = length,
                SectorSize = 512,
                DiskSize = fileLength,
                Model = Path.GetFileName(imagePath),
                PartitionType = type,
                ImagePath = imagePath,
                FileSystem = fs
            });
        }

        return result;
    }

    private static string NormalizeLetter(string value)
    {
        value = value.Trim();
        if (value.Length == 0) return "";
        char c = char.ToUpperInvariant(value[0]);
        return c is >= 'A' and <= 'Z' ? c.ToString() : "";
    }
}
