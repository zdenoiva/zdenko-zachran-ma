using NtfsRescue.Util;

namespace NtfsRescue.Core;

public sealed class NtfsVolume : INtfsSource
{
    public RawDisk Disk { get; }
    public long PartitionOffset { get; }
    public long PartitionLength { get; }

    public NtfsBootSector Boot { get; }
    public NtfsBootSector? PrimaryBoot { get; }
    public NtfsBootSector? BackupBoot { get; }
    public byte[] PrimaryBootRaw { get; }
    public byte[]? BackupBootRaw { get; }
    public bool BootFromBackup { get; }

    public int ClusterSize => Boot.ClusterSize;
    public int RecordSize => Boot.MftRecordSize;
    public long TotalRecords { get; }
    public long MftRealSize { get; }
    public IReadOnlyList<NtfsRun> MftRuns => _mftRuns;

    private readonly List<NtfsRun> _mftRuns;
    private readonly Dictionary<ulong, NtfsRecord> _cache = new();

    private NtfsVolume(RawDisk disk, long offset, long length, NtfsBootSector boot,
        byte[] primaryRaw, NtfsBootSector? primary, byte[]? backupRaw, NtfsBootSector? backup,
        bool fromBackup, List<NtfsRun> mftRuns, long mftRealSize, long totalRecords)
    {
        Disk = disk;
        PartitionOffset = offset;
        PartitionLength = length;
        Boot = boot;
        PrimaryBoot = primary;
        PrimaryBootRaw = primaryRaw;
        BackupBoot = backup;
        BackupBootRaw = backupRaw;
        BootFromBackup = fromBackup;
        _mftRuns = mftRuns;
        MftRealSize = mftRealSize;
        TotalRecords = totalRecords;
    }

    public static NtfsVolume Open(RawDisk disk, long partitionOffset, long partitionLength)
    {
        var primaryRaw = disk.ReadBytes(partitionOffset, 512);
        var primary = NtfsBootSector.Parse(primaryRaw);

        long effectiveLength = partitionLength;
        if (effectiveLength <= 0 && primary.IsValid)
            effectiveLength = primary.VolumeSize;

        byte[]? backupRaw = null;
        NtfsBootSector? backup = null;
        if (effectiveLength > 512)
        {
            try
            {
                long backupOffset = partitionOffset + effectiveLength - disk.SectorSize;
                backupRaw = disk.ReadBytes(backupOffset, 512);
                backup = NtfsBootSector.Parse(backupRaw);
            }
            catch (Exception ex)
            {
                Log.Warn("Záložný boot sector sa nepodarilo prečítať: " + ex.Message);
            }
        }

        NtfsBootSector boot;
        bool fromBackup;
        if (primary.IsValid)
        {
            boot = primary;
            fromBackup = false;
        }
        else if (backup is { IsValid: true })
        {
            boot = backup;
            fromBackup = true;
            Log.Warn("Primárny boot sector je poškodený, používam záložný.");
        }
        else
        {
            throw new InvalidDataException(
                $"Na partícii sa nenašiel platný NTFS boot sector. Primárny: {primary.Error}; záložný: {backup?.Error ?? "chýba"}.");
        }

        if (effectiveLength <= 0) effectiveLength = boot.VolumeSize;
        if (effectiveLength <= 0) effectiveLength = disk.Size - partitionOffset;

        var rec0 = disk.ReadBytes(partitionOffset + boot.MftLcn * boot.ClusterSize, boot.MftRecordSize);
        var rec = NtfsRecord.TryParse(rec0, 0, boot.BytesPerSector, out var recError);
        if (rec is null)
            throw new InvalidDataException($"Nepodarilo sa prečítať $MFT záznam 0: {recError}");

        var data = rec.DataAttribute();
        if (data is null || data.NonResident == false || data.Runs.Count == 0)
            throw new InvalidDataException("$MFT nemá platný ne-residentný $DATA atribút.");

        long mftSize = data.RealSize > 0 ? data.RealSize : data.AllocatedSize;
        long totalRecords = mftSize / boot.MftRecordSize;
        if (totalRecords <= 0)
            throw new InvalidDataException("$MFT má nulovú veľkosť.");

        // Poistka proti nezmyselne veľkému MFT.
        totalRecords = Math.Min(totalRecords, 32_000_000);

        // Ak je MFT fragmentované, prvý záznam sme čítali priamo z boot sectoru;
        // ďalšie záznamy už idú cez runlist.
        Log.Info($"NTFS otvorené: {boot.Describe()}, záznamov v $MFT: {totalRecords:N0}");

        return new NtfsVolume(disk, partitionOffset, effectiveLength, boot, primaryRaw, primary,
            backupRaw, backup, fromBackup, data.Runs, mftSize, totalRecords);
    }

    public byte[] ReadPartitionBytes(long offset, int length)
    {
        var buf = new byte[length];
        Disk.ReadAt(PartitionOffset + offset, buf, length);
        return buf;
    }

    public byte[] ReadMftRecordRaw(ulong index, bool cache)
    {
        if (index >= (ulong)TotalRecords && TotalRecords > 0)
            throw new ArgumentOutOfRangeException(nameof(index));

        var data = new byte[RecordSize];
        ReadStreamBytes(_mftRuns, (long)index * RecordSize, data, 0, RecordSize);
        return data;
    }

    public NtfsRecord? ReadMftRecord(ulong index)
    {
        var raw = ReadMftRecordRaw(index, cache: false);
        return NtfsRecord.TryParse(raw, index, Boot.BytesPerSector, out _);
    }

    public NtfsRecord? ReadRecordCached(ulong index)
    {
        if (_cache.TryGetValue(index, out var cached)) return cached;

        var raw = ReadMftRecordRaw(index, cache: true);
        var rec = NtfsRecord.TryParse(raw, index, Boot.BytesPerSector, out _);
        if (rec is not null)
        {
            if (_cache.Count > 20000) _cache.Clear();
            _cache[index] = rec;
        }
        return rec;
    }

    public NtfsRecord? ReadRecord(ulong number) => ReadRecordCached(number);

    public void ReadStreamBytes(IReadOnlyList<NtfsRun> runs, long offset, byte[] buffer, int bufferOffset, int length)
        => NtfsIo.ReadStreamBytes(Disk, PartitionOffset, ClusterSize, runs, offset, buffer, bufferOffset, length);

    public void ReadClusters(long lcn, long count, byte[] buffer, int bufferOffset)
        => NtfsIo.ReadClusters(Disk, PartitionOffset, ClusterSize, lcn, count, buffer, bufferOffset);

    public string BootSummary()
    {
        var lines = new List<string>
        {
            Boot.Describe()
        };
        lines.Add($"primárny boot sector: {(PrimaryBoot?.IsValid == true ? "OK" : PrimaryBoot?.Error ?? "nečitateľný")}");
        lines.Add($"záložný boot sector: {(BackupBoot?.IsValid == true ? "OK" : BackupBoot?.Error ?? "nečitateľný")}");
        if (PrimaryBoot is { IsValid: true } && BackupBoot is { IsValid: true })
            lines.Add("porovnanie primárny/záložný: " + PrimaryBoot.Compare(BackupBoot));
        if (BootFromBackup) lines.Add("Pozor: používa sa záložný boot sector.");
        return string.Join(Environment.NewLine, lines);
    }
}
