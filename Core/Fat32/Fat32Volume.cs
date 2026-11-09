namespace NtfsRescue.Core.Fat32;

public sealed class Fat32Volume
{
    public RawDisk Disk { get; }
    public long PartitionOffset { get; }
    public Fat32BootSector Boot { get; }

    private byte[]? _fat;
    private bool _useFat2;
    private long _fatBytes;

    public bool Fat1Valid { get; private set; }
    public bool Fat2Valid { get; private set; }
    public bool UseFat => Fat1Valid || Fat2Valid;
    public string FatStatus { get; private set; } = "neoverené";

    public Fat32Volume(RawDisk disk, long partitionOffset, Fat32BootSector boot)
    {
        Disk = disk;
        PartitionOffset = partitionOffset;
        Boot = boot;
    }

    public void LoadFat(CancellationToken ct)
    {
        _fatBytes = Boot.FatSizeBytes;
        if (_fatBytes <= 0)
        {
            FatStatus = "FAT má nulovú veľkosť";
            return;
        }

        if (_fatBytes <= 512L * 1024 * 1024)
        {
            var fat1 = ReadFatBytes(0, ct);
            Fat1Valid = ValidateFat(fat1);
            if (!Fat1Valid)
            {
                var fat2 = ReadFatBytes(Boot.FatSizeBytes, ct);
                Fat2Valid = ValidateFat(fat2);
                if (Fat2Valid)
                {
                    _fat = fat2;
                    _useFat2 = true;
                    FatStatus = "FAT1 poškodená, používam FAT2";
                }
                else
                {
                    FatStatus = "obe FAT poškodené – použijem súvislé čítanie";
                }
            }
            else
            {
                _fat = fat1;
                FatStatus = "FAT1 v poriadku";
                Fat2Valid = ValidateFat(ReadFatBytes(Boot.FatSizeBytes, ct));
            }
        }
        else
        {
            // Príliš veľká FAT na načítanie do pamäte; overíme len prvé záznamy.
            Fat1Valid = ValidateFatHeader(ReadFatHeader(0));
            Fat2Valid = ValidateFatHeader(ReadFatHeader(Boot.FatSizeBytes));
            _useFat2 = !Fat1Valid && Fat2Valid;
            FatStatus = Fat1Valid ? "FAT1 v poriadku (on-demand)"
                : Fat2Valid ? "FAT1 poškodená, používam FAT2 (on-demand)"
                : "obe FAT poškodené – použijem súvislé čítanie";
        }
    }

    public uint GetFatEntry(uint cluster)
    {
        if (!UseFat || cluster >= Boot.ClusterCount + 2) return 0x0FFFFFFF;

        if (_fat is not null)
        {
            int offset = checked((int)(cluster * 4));
            if (offset + 4 > _fat.Length) return 0x0FFFFFFF;
            return BitConverter.ToUInt32(_fat, offset) & 0x0FFFFFFF;
        }

        var buf = new byte[4];
        long offsetOnDisk = PartitionOffset + Boot.FatStartOffset +
                            (_useFat2 ? Boot.FatSizeBytes : 0) + cluster * 4L;
        try
        {
            Disk.ReadAt(offsetOnDisk, buf, 4);
            return BitConverter.ToUInt32(buf, 0) & 0x0FFFFFFF;
        }
        catch
        {
            return 0x0FFFFFFF;
        }
    }

    public IEnumerable<uint> Chain(uint start, CancellationToken ct)
    {
        var seen = new HashSet<uint>();
        uint cluster = start;
        long guard = 0;

        while (cluster >= 2 && cluster < Boot.ClusterCount + 2)
        {
            ct.ThrowIfCancellationRequested();
            if (!seen.Add(cluster)) yield break;
            yield return cluster;

            uint next = GetFatEntry(cluster);
            if (next == 0 || next == 0x0FFFFFF7 || next >= 0x0FFFFFF8) yield break;
            cluster = next;
            if (++guard > Boot.ClusterCount + 1) yield break;
        }
    }

    public byte[] ReadCluster(uint cluster)
    {
        if (cluster < 2 || cluster >= Boot.ClusterCount + 2)
            throw new IOException($"Neplatný cluster {cluster}.");

        var buffer = new byte[Boot.ClusterSize];
        long offset = PartitionOffset + Boot.DataStartOffset + (cluster - 2L) * Boot.ClusterSize;
        Disk.ReadAt(offset, buffer, buffer.Length);
        return buffer;
    }

    public IEnumerable<Fat32DirEntry> EnumerateDirectory(uint startCluster, CancellationToken ct)
    {
        if (startCluster < 2) yield break;

        if (UseFat)
        {
            foreach (var cluster in Chain(startCluster, ct))
            {
                byte[] data;
                try { data = ReadCluster(cluster); }
                catch { yield break; }

                for (int off = 0; off + 32 <= data.Length; off += 32)
                {
                    var raw = new byte[32];
                    Buffer.BlockCopy(data, off, raw, 0, 32);
                    if (raw[0] == 0x00) yield break;
                    yield return Fat32DirEntry.Parse(raw);
                }
            }
        }
        else
        {
            // FAT je nepoužiteľná: čítame adresár súvislo, kým nenarazíme na koniec.
            for (uint i = 0; i < 256; i++)
            {
                uint cluster = startCluster + i;
                byte[] data;
                try { data = ReadCluster(cluster); }
                catch { yield break; }

                for (int off = 0; off + 32 <= data.Length; off += 32)
                {
                    var raw = new byte[32];
                    Buffer.BlockCopy(data, off, raw, 0, 32);
                    if (raw[0] == 0x00) yield break;
                    yield return Fat32DirEntry.Parse(raw);
                }
            }
        }
    }

    public void ReadFile(Fat32Entry entry, Stream output, Action<long>? progress,
        CancellationToken ct, Action<string>? warn)
    {
        if (entry.Size <= 0) return;
        if (entry.FirstCluster < 2)
            throw new IOException("Súbor nemá platný prvý cluster.");

        long total = entry.Size;
        long written = 0;
        bool fatUsable = UseFat && GetFatEntry(entry.FirstCluster) != 0 &&
                         GetFatEntry(entry.FirstCluster) != 0x0FFFFFF7;

        if (fatUsable)
        {
            foreach (var cluster in Chain(entry.FirstCluster, ct))
            {
                ct.ThrowIfCancellationRequested();
                byte[] data;
                try { data = ReadCluster(cluster); }
                catch (Exception ex)
                {
                    warn?.Invoke($"Čítanie klastra {cluster} zlyhalo: {ex.Message}");
                    break;
                }

                int chunk = (int)Math.Min(data.Length, total - written);
                if (chunk <= 0) break;
                output.Write(data, 0, chunk);
                written += chunk;
                progress?.Invoke(written);
                if (written >= total) break;
            }

            if (written < total)
            {
                warn?.Invoke($"FAT reťazec je kratší než veľkosť súboru ({written} z {total} B); dopĺňam nuly.");
                WriteZeros(output, total - written, ct);
            }
            return;
        }

        warn?.Invoke("FAT reťazec chýba alebo je poškodený – čítam súbor súvislo od prvého klastra.");
        long offset = PartitionOffset + Boot.DataStartOffset + (entry.FirstCluster - 2L) * Boot.ClusterSize;
        long remaining = Math.Min(total, Boot.VolumeSize - (entry.FirstCluster - 2L) * Boot.ClusterSize);
        var buffer = new byte[1 << 20];

        while (remaining > 0)
        {
            ct.ThrowIfCancellationRequested();
            int chunk = (int)Math.Min(buffer.Length, remaining);
            Disk.ReadAt(offset, buffer, chunk);
            output.Write(buffer, 0, chunk);
            offset += chunk;
            remaining -= chunk;
            written += chunk;
            progress?.Invoke(written);
        }

        if (written < total)
            WriteZeros(output, total - written, ct);
    }

    private byte[] ReadFatBytes(long extraOffset, CancellationToken ct)
    {
        int size = checked((int)Math.Min(_fatBytes, 512L * 1024 * 1024));
        var buffer = new byte[size];
        ct.ThrowIfCancellationRequested();
        Disk.ReadAt(PartitionOffset + Boot.FatStartOffset + extraOffset, buffer, buffer.Length);
        return buffer;
    }

    private byte[] ReadFatHeader(long extraOffset)
    {
        var buffer = new byte[8];
        try
        {
            Disk.ReadAt(PartitionOffset + Boot.FatStartOffset + extraOffset, buffer, buffer.Length);
        }
        catch { }
        return buffer;
    }

    private static bool ValidateFat(byte[] fat)
    {
        if (fat.Length < 8) return false;
        uint first = BitConverter.ToUInt32(fat, 0) & 0x0FFFFFFF;
        uint second = BitConverter.ToUInt32(fat, 4) & 0x0FFFFFFF;
        if ((first & 0x0FFFFFF8) != 0x0FFFFFF8) return false;
        if ((second & 0x0FFFFFF8) != 0x0FFFFFF8) return false;
        return true;
    }

    private static bool ValidateFatHeader(byte[] header) => ValidateFat(header);

    private static void WriteZeros(Stream output, long count, CancellationToken ct)
    {
        int bufferSize = (int)Math.Min(1 << 20, Math.Max(0, count));
        var zeros = new byte[bufferSize];
        while (count > 0)
        {
            ct.ThrowIfCancellationRequested();
            int chunk = (int)Math.Min(zeros.Length, count);
            if (chunk <= 0) break;
            output.Write(zeros, 0, chunk);
            count -= chunk;
        }
    }
}
