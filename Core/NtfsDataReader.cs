using System.Text;
using NtfsRescue.Util;

namespace NtfsRescue.Core;

/// <summary>
/// Sekvencne citanie obsahu suboru z NTFS (resident, runlist, kompresia, sparse).
/// Nikdy nezapisuje na zdrojovy disk.
/// </summary>
public sealed class NtfsDataReader
{
    private readonly INtfsSource _volume;
    private readonly NtfsEntry _entry;

    private readonly List<NtfsRun> _runs = new();
    private NtfsAttribute? _dataAttribute;
    private byte[]? _resident;
    private bool _compressed;
    private bool _prepared;

    public long Length { get; private set; }
    public bool IsCompressed => _compressed;

    public NtfsDataReader(INtfsSource volume, NtfsEntry entry)
    {
        _volume = volume;
        _entry = entry;
    }

    public void Prepare()
    {
        if (_prepared) return;
        _prepared = true;

        if (_entry.IsDirectory) { Length = 0; return; }

        var rec = _volume.ReadRecord(_entry.RecordNumber)
                  ?? throw new InvalidDataException($"MFT záznam {_entry.RecordNumber} sa nedá prečítať.");

        if (rec.BaseRecord != 0)
        {
            var baseNumber = rec.BaseRecord & 0xFFFFFFFFFFFF;
            rec = _volume.ReadRecord(baseNumber)
                  ?? throw new InvalidDataException($"Základný MFT záznam {baseNumber} sa nedá prečítať.");
        }

        var dataAttrs = new List<NtfsAttribute>();
        var direct = rec.DataAttribute();
        if (direct is not null) dataAttrs.Add(direct);

        var attrList = rec.FindFirst(AttrType.AttributeList);
        if (attrList is not null)
        {
            try
            {
                var listBytes = ReadAttributeValue(attrList, 64 * 1024 * 1024);
                foreach (var item in ParseAttributeList(listBytes))
                {
                    if (item.Type != AttrType.Data || item.NameLength != 0) continue;

                    var ext = _volume.ReadRecord(item.FileReference & 0xFFFFFFFFFFFF);
                    if (ext is null) continue;

                    var match = ext.Attributes.FirstOrDefault(a =>
                        a.Type == AttrType.Data && a.Name.Length == 0 && a.AttributeId == item.AttributeId);
                    if (match is not null && !dataAttrs.Contains(match))
                        dataAttrs.Add(match);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Zoznam atribútov záznamu {_entry.RecordNumber} sa nedá prečítať: {ex.Message}");
            }
        }

        if (dataAttrs.Count == 0)
        {
            Length = 0;
            return;
        }

        var resident = dataAttrs.FirstOrDefault(a => !a.NonResident && a.ResidentValue is not null);
        if (resident is not null && dataAttrs.Count == 1)
        {
            _resident = resident.ResidentValue;
            Length = _resident?.Length ?? 0;
            return;
        }

        _dataAttribute = dataAttrs
            .Where(a => a.NonResident)
            .OrderByDescending(a => a.RealSize)
            .FirstOrDefault();

        if (_dataAttribute is null)
        {
            Length = 0;
            return;
        }

        Length = Math.Max(0, _dataAttribute.RealSize);
        _compressed = _entry.Compressed || _dataAttribute.CompressionUnit > 0;

        foreach (var a in dataAttrs.Where(a => a.NonResident))
            _runs.AddRange(a.Runs);

        _runs.Sort((a, b) => a.Vcn.CompareTo(b.Vcn));

        // Zluc susedne runy, aby citanie bolo rychlejsie.
        var merged = new List<NtfsRun>();
        foreach (var r in _runs)
        {
            if (merged.Count == 0) { merged.Add(r); continue; }
            var last = merged[^1];
            if (last.Lcn >= 0 && r.Lcn >= 0 && last.Vcn + last.Clusters == r.Vcn &&
                last.Lcn + last.Clusters == r.Lcn)
            {
                merged[^1] = last with { Clusters = last.Clusters + r.Clusters };
            }
            else if (last.IsSparse && r.IsSparse && last.Vcn + last.Clusters == r.Vcn)
            {
                merged[^1] = last with { Clusters = last.Clusters + r.Clusters };
            }
            else
            {
                merged.Add(r);
            }
        }
        _runs.Clear();
        _runs.AddRange(merged);

        if (Length == 0 && _dataAttribute.RealSize == 0)
            Length = 0;
    }

    public void CopyTo(Stream output, Action<long>? progress, CancellationToken ct, Action<string>? warn)
    {
        Prepare();
        if (Length <= 0) return;

        if (_resident is not null)
        {
            output.Write(_resident, 0, _resident.Length);
            progress?.Invoke(_resident.Length);
            return;
        }

        if (_entry.Encrypted)
            throw new NotSupportedException("Súbor je šifrovaný EFS; bez kľúča ho nemožno prečítať.");

        if (_compressed)
            CopyCompressed(output, progress, ct, warn);
        else
            CopyRuns(output, progress, ct, warn);
    }

    private void CopyRuns(Stream output, Action<long>? progress, CancellationToken ct, Action<string>? warn)
    {
        const int bufferSize = 1 << 20;
        var buffer = new byte[bufferSize];
        long offset = 0;

        while (offset < Length)
        {
            ct.ThrowIfCancellationRequested();
            int chunk = (int)Math.Min(bufferSize, Length - offset);
            try
            {
                _volume.ReadStreamBytes(_runs, offset, buffer, 0, chunk);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                warn?.Invoke($"Čítanie zlyhalo na offsete {offset}: {ex.Message}");
                throw new IOException($"čítanie zlyhalo na offsete {offset}: {ex.Message}", ex);
            }

            output.Write(buffer, 0, chunk);
            offset += chunk;
            progress?.Invoke(offset);
        }
    }

    private void CopyCompressed(Stream output, Action<long>? progress, CancellationToken ct, Action<string>? warn)
    {
        int clusterSize = _volume.ClusterSize;
        int unitClusters = _dataAttribute?.CompressionUnit ?? 0;
        if (unitClusters <= 0) unitClusters = 16;

        long unitBytes = (long)unitClusters * clusterSize;
        if (unitBytes <= 0 || unitBytes > 256L * 1024 * 1024)
            throw new InvalidDataException("Neplatná kompresná jednotka.");

        long totalUnits = (Length + unitBytes - 1) / unitBytes;
        long written = 0;

        for (long unit = 0; unit < totalUnits; unit++)
        {
            ct.ThrowIfCancellationRequested();

            long unitStartVcn = unit * unitClusters;
            long unitStartByte = unit * unitBytes;
            int bytesToWrite = (int)Math.Min(unitBytes, Length - unitStartByte);

            var allocated = new List<(NtfsRun Run, long StartVcn, long Clusters)>();
            long allocatedClusters = 0;

            foreach (var run in _runs)
            {
                long start = Math.Max(run.Vcn, unitStartVcn);
                long end = Math.Min(run.EndVcn, unitStartVcn + unitClusters);
                if (end <= start) continue;
                if (run.IsSparse) continue;
                allocated.Add((run, start, end - start));
                allocatedClusters += end - start;
            }

            if (allocatedClusters == 0)
            {
                WriteZeros(output, bytesToWrite, ct);
            }
            else if (allocatedClusters >= unitClusters)
            {
                var raw = new byte[unitClusters * clusterSize];
                ReadUnitClusters(unitStartVcn, unitClusters, raw);
                output.Write(raw, 0, bytesToWrite);
            }
            else
            {
                int compSize = checked((int)(allocatedClusters * clusterSize));
                var comp = new byte[compSize];
                int dest = 0;
                foreach (var (run, startVcn, clusters) in allocated)
                {
                    int bytes = checked((int)(clusters * clusterSize));
                    if (dest + bytes > comp.Length) bytes = comp.Length - dest;
                    if (bytes <= 0) break;
                    long lcn = run.Lcn + (startVcn - run.Vcn);
                    _volume.ReadClusters(lcn, clusters, comp, dest);
                    dest += bytes;
                }

                var decompressed = Lznt1.Decompress(comp, (int)unitBytes);
                int toWrite = Math.Min(bytesToWrite, decompressed.Length);
                if (toWrite > 0) output.Write(decompressed, 0, toWrite);
                if (toWrite < bytesToWrite)
                    WriteZeros(output, bytesToWrite - toWrite, ct);
            }

            written += bytesToWrite;
            progress?.Invoke(written);
        }
    }

    private void ReadUnitClusters(long startVcn, int count, byte[] buffer)
    {
        Array.Clear(buffer, 0, buffer.Length);
        long endVcn = startVcn + count;

        foreach (var run in _runs)
        {
            long start = Math.Max(run.Vcn, startVcn);
            long end = Math.Min(run.EndVcn, endVcn);
            if (end <= start) continue;
            if (run.IsSparse) continue;

            int destOffset = checked((int)((start - startVcn) * _volume.ClusterSize));
            long lcn = run.Lcn + (start - run.Vcn);
            _volume.ReadClusters(lcn, end - start, buffer, destOffset);
        }
    }

    private static void WriteZeros(Stream output, int count, CancellationToken ct)
    {
        var zeros = new byte[Math.Min(count, 1 << 20)];
        int left = count;
        while (left > 0)
        {
            ct.ThrowIfCancellationRequested();
            int chunk = Math.Min(left, zeros.Length);
            output.Write(zeros, 0, chunk);
            left -= chunk;
        }
    }

    private byte[] ReadAttributeValue(NtfsAttribute attr, int maxBytes)
    {
        if (attr.ResidentValue is not null) return attr.ResidentValue;

        long length = Math.Min(attr.RealSize, maxBytes);
        if (length <= 0) return Array.Empty<byte>();

        var buffer = new byte[(int)length];
        _volume.ReadStreamBytes(attr.Runs, 0, buffer, 0, (int)length);
        return buffer;
    }

    private sealed record AttributeListEntry(
        uint Type,
        byte NameLength,
        ulong LowestVcn,
        ulong FileReference,
        ushort AttributeId);

    private static IEnumerable<AttributeListEntry> ParseAttributeList(byte[] data)
    {
        int pos = 0;
        while (pos + 26 <= data.Length)
        {
            uint type = Bin.U32(data, pos);
            int length = Bin.U16(data, pos + 4);
            if (type == AttrType.End || length < 26 || pos + length > data.Length) yield break;

            byte nameLength = data[pos + 6];
            ulong lowestVcn = Bin.U64(data, pos + 8);
            ulong fileRef = Bin.U64(data, pos + 16);
            ushort id = Bin.U16(data, pos + 24);

            yield return new AttributeListEntry(type, nameLength, lowestVcn, fileRef, id);
            pos += length;
        }
    }
}
