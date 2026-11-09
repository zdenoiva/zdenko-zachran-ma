namespace NtfsRescue.Core;

/// <summary>
/// Read-only pristup k fyzickemu disku cez \\.\PhysicalDriveN.
/// Cita sa po sektoroch; pozadovany rozsah sa zvacsí na hranice sektora.
/// </summary>
public sealed class RawDisk : IDisposable
{
    private readonly FileStream _stream;
    private readonly object _gate = new();

    public string DevicePath { get; }
    public int DiskNumber { get; }
    public int SectorSize { get; }
    public long Size { get; }

    public RawDisk(int diskNumber, int sectorSize, long size)
    {
        if (diskNumber < 0) throw new ArgumentOutOfRangeException(nameof(diskNumber));
        if (sectorSize is not (512 or 1024 or 2048 or 4096))
            sectorSize = 512;

        DiskNumber = diskNumber;
        SectorSize = sectorSize;
        Size = size;
        DevicePath = $@"\\.\PhysicalDrive{diskNumber}";

        try
        {
            _stream = new FileStream(
                DevicePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                1 << 16,
                FileOptions.None);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new IOException(
                $"Prístup k {DevicePath} bol zamietnutý. Spustite program ako administrátor.", ex);
        }
        catch (Exception ex)
        {
            throw new IOException($"Nepodarilo sa otvoriť {DevicePath}: {ex.Message}", ex);
        }
    }

    private RawDisk(string filePath, int sectorSize)
    {
        if (sectorSize is not (512 or 1024 or 2048 or 4096))
            sectorSize = 512;

        DiskNumber = -1;
        SectorSize = sectorSize;
        Size = new FileInfo(filePath).Length;
        DevicePath = filePath;
        _stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
            1 << 16, FileOptions.None);
    }

    public static RawDisk OpenFile(string path, int sectorSize = 512)
        => new(path, sectorSize);

    public static RawDisk Open(int diskNumber, int sectorSize = 512, long size = 0)
        => new(diskNumber, sectorSize, size);

    public void ReadAt(long offset, byte[] buffer, int length)
        => ReadAt(offset, buffer, 0, length);

    public void ReadAt(long offset, byte[] buffer, int bufferOffset, int length)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (bufferOffset < 0 || bufferOffset > buffer.Length) throw new ArgumentOutOfRangeException(nameof(bufferOffset));
        if (length < 0 || bufferOffset + length > buffer.Length) throw new ArgumentOutOfRangeException(nameof(length));
        if (length == 0) return;

        long start = AlignDown(offset);
        long end = offset + length;
        long alignedEnd = AlignUp(end);
        int readLength = checked((int)(alignedEnd - start));

        if (start == offset && readLength == length)
        {
            ReadAligned(start, buffer.AsSpan(bufferOffset, length));
            return;
        }

        var temp = new byte[readLength];
        ReadAligned(start, temp, readLength);
        Buffer.BlockCopy(temp, (int)(offset - start), buffer, bufferOffset, length);
    }

    public void ReadAt(long offset, Span<byte> buffer)
    {
        var tmp = new byte[buffer.Length];
        ReadAt(offset, tmp, tmp.Length);
        tmp.CopyTo(buffer);
    }

    public byte[] ReadBytes(long offset, int length)
    {
        var buf = new byte[length];
        ReadAt(offset, buf, length);
        return buf;
    }

    private void ReadAligned(long offset, byte[] buffer, int length)
    {
        lock (_gate)
        {
            _stream.Position = offset;
            _stream.ReadExactly(buffer, 0, length);
        }
    }

    private void ReadAligned(long offset, Span<byte> buffer)
    {
        lock (_gate)
        {
            _stream.Position = offset;
            _stream.ReadExactly(buffer);
        }
    }

    private long AlignDown(long value) => value - (value % SectorSize);

    private long AlignUp(long value)
    {
        var rem = value % SectorSize;
        return rem == 0 ? value : value + (SectorSize - rem);
    }

    public void Dispose()
    {
        try { _stream.Dispose(); } catch { }
    }
}
