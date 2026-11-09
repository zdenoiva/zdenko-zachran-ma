using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NtfsRescue.Util;

namespace NtfsRescue.Core;

public sealed record VolumeLocation(
    int DiskNumber,
    long Offset,
    long Length,
    int SectorSize,
    long DiskSize,
    string Model,
    string Serial,
    int BusType)
{
    public bool IsValid => DiskNumber >= 0;
}

/// <summary>
/// Zisti, na ktorom fyzickom disku a s akym offsetom sa nachadza volume.
/// Primarne cez DeviceIoControl, fallback cez PowerShell (Get-Partition).
/// </summary>
public static class VolumeMapper
{
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint IoctlVolumeGetVolumeDiskExtents = 0x00560000;
    private const uint IoctlDiskGetDriveGeometryEx = 0x000700A0;
    private const uint IoctlStorageQueryProperty = 0x002D1400;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        int nInBufferSize,
        IntPtr lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    public static VolumeLocation Locate(char letter)
    {
        letter = char.ToUpperInvariant(letter);
        try
        {
            var byIoctl = LocateByIoctl(letter);
            if (byIoctl is not null) return byIoctl;
        }
        catch (Exception ex)
        {
            Log.Warn($"DeviceIoControl pre {letter}: zlyhal: {ex.Message}");
        }

        try
        {
            var byPs = LocateByPowerShell(letter).GetAwaiter().GetResult();
            if (byPs is not null) return byPs;
        }
        catch (Exception ex)
        {
            Log.Warn($"PowerShell lokalizacia {letter}: zlyhala: {ex.Message}");
        }

        return new VolumeLocation(-1, 0, 0, 512, 0, "", "", 0);
    }

    public static int GetDiskNumberForPath(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root) || root.Length < 2 || root[1] != ':') return -1;
            var loc = Locate(root[0]);
            return loc.DiskNumber;
        }
        catch
        {
            return -1;
        }
    }

    private static VolumeLocation? LocateByIoctl(char letter)
    {
        var volumePath = $@"\\.\{letter}:";

        var extentBuf = IoControl(volumePath, IoctlVolumeGetVolumeDiskExtents, null, 8 + 24 * 64);
        if (extentBuf is null) return null;

        int count = BitConverter.ToInt32(extentBuf, 0);
        if (count <= 0 || count > 64) return null;

        int diskNumber = BitConverter.ToInt32(extentBuf, 8);
        long offset = BitConverter.ToInt64(extentBuf, 8 + 8);
        long length = BitConverter.ToInt64(extentBuf, 8 + 16);

        var diskPath = $@"\\.\PhysicalDrive{diskNumber}";
        int sectorSize = 512;
        long diskSize = 0;

        var geo = IoControl(diskPath, IoctlDiskGetDriveGeometryEx, null, 256);
        if (geo is not null && geo.Length >= 32)
        {
            int s = BitConverter.ToInt32(geo, 20);
            if (s is 512 or 1024 or 2048 or 4096) sectorSize = s;
            diskSize = BitConverter.ToInt64(geo, 24);
        }

        var (model, serial, busType) = ReadDescriptor(diskPath);

        return new VolumeLocation(diskNumber, offset, length, sectorSize, diskSize, model, serial, busType);
    }

    private static async Task<VolumeLocation?> LocateByPowerShell(char letter)
    {
        var partJson = await Ps.RunJsonAsync(
            $"$p = Get-Partition -DriveLetter {letter} -ErrorAction Stop | Select-Object DiskNumber,Offset,Size | ConvertTo-Json -Compress; $p",
            CancellationToken.None, 30000).ConfigureAwait(false);
        if (partJson is null) return null;

        var part = Ps.Objects(partJson).FirstOrDefault();
        if (part.ValueKind != System.Text.Json.JsonValueKind.Object) return null;

        int diskNumber = Ps.Int(part, "DiskNumber");
        long offset = Ps.Long(part, "Offset");
        long length = Ps.Long(part, "Size");
        if (diskNumber < 0 || length <= 0) return null;

        int sectorSize = 512;
        long diskSize = 0;
        string model = "";
        string serial = "";
        int busType = 0;

        var diskJson = await Ps.RunJsonAsync(
            $"$d = Get-Disk -Number {diskNumber} -ErrorAction Stop | Select-Object FriendlyName,SerialNumber,Size,LogicalSectorSize,PhysicalSectorSize,BusType | ConvertTo-Json -Compress; $d",
            CancellationToken.None, 30000).ConfigureAwait(false);
        if (diskJson is not null)
        {
            var d = Ps.Objects(diskJson).FirstOrDefault();
            if (d.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                model = Ps.Str(d, "FriendlyName");
                serial = Ps.Str(d, "SerialNumber");
                diskSize = Ps.Long(d, "Size");
                int ls = Ps.Int(d, "LogicalSectorSize");
                if (ls is 512 or 1024 or 2048 or 4096) sectorSize = ls;
                var bt = Ps.Str(d, "BusType");
                busType = bt switch
                {
                    "USB" => 7,
                    "NVMe" => 17,
                    _ => 0
                };
            }
        }

        return new VolumeLocation(diskNumber, offset, length, sectorSize, diskSize, model, serial, busType);
    }

    private static (string Model, string Serial, int BusType) ReadDescriptor(string diskPath)
    {
        var query = new byte[12];
        var buf = IoControl(diskPath, IoctlStorageQueryProperty, query, 1024);
        if (buf is null) return ("", "", 0);

        int size = BitConverter.ToInt32(buf, 4);
        int limit = Math.Min(size > 0 ? size : buf.Length, buf.Length);
        if (limit < 36) return ("", "", 0);

        string vendor = AsciiField(buf, 12, limit);
        string product = AsciiField(buf, 16, limit);
        string serial = AsciiField(buf, 24, limit);
        int bus = BitConverter.ToInt32(buf, 28);

        var model = string.Join(" ", new[] { vendor, product }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        return (model, serial.Trim(), bus);
    }

    private static string AsciiField(byte[] buf, int offsetField, int limit)
    {
        int off = BitConverter.ToInt32(buf, offsetField);
        if (off <= 0 || off >= limit) return "";

        int end = off;
        while (end < limit && buf[end] != 0) end++;
        return System.Text.Encoding.ASCII.GetString(buf, off, end - off).Trim();
    }

    private static byte[]? IoControl(string devicePath, uint code, byte[]? input, int outputSize)
    {
        using var handle = CreateFileW(
            devicePath,
            GenericRead,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            Log.Warn($"CreateFile {devicePath} zlyhal, chyba {err}.");
            return null;
        }

        IntPtr inPtr = IntPtr.Zero;
        IntPtr outPtr = IntPtr.Zero;
        try
        {
            int inSize = 0;
            if (input is { Length: > 0 })
            {
                inSize = input.Length;
                inPtr = Marshal.AllocHGlobal(inSize);
                Marshal.Copy(input, 0, inPtr, inSize);
            }

            outPtr = Marshal.AllocHGlobal(outputSize);
            if (!DeviceIoControl(handle, code, inPtr, inSize, outPtr, outputSize,
                    out int returned, IntPtr.Zero))
            {
                return null;
            }

            if (returned <= 0) return null;
            var result = new byte[returned];
            Marshal.Copy(outPtr, result, 0, returned);
            return result;
        }
        finally
        {
            if (inPtr != IntPtr.Zero) Marshal.FreeHGlobal(inPtr);
            if (outPtr != IntPtr.Zero) Marshal.FreeHGlobal(outPtr);
        }
    }
}
