using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NtfsRescue.Util;

namespace NtfsRescue.Repair;

/// <summary>
/// Zápis do volume (nie do PhysicalDrive) po uzamknutí a odpojení.
/// Používa sa na boot sector, FAT1 a NTFS record 0.
/// </summary>
public static class VolumeWriter
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FsctlLockVolume = 0x00090018;
    private const uint FsctlUnlockVolume = 0x0009001C;
    private const uint FsctlDismountVolume = 0x00090020;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, int nInBufferSize,
        IntPtr lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFilePointerEx(
        SafeFileHandle hFile, long liDistanceToMove, out long lpNewFilePointer, uint dwMoveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteFile(
        SafeFileHandle hFile, byte[] lpBuffer, int nNumberOfBytesToWrite,
        out int lpNumberOfBytesWritten, IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushFileBuffers(SafeFileHandle hFile);

    public static void WriteAt(char letter, long offset, byte[] data,
        Action<string> log, CancellationToken ct)
    {
        if (data.Length == 0) return;
        var path = $@"\\.\{char.ToUpperInvariant(letter)}:";
        log($"Otváram {path} pre zápis na offset {offset:N0}...");

        using var handle = CreateFileW(path, GenericRead | GenericWrite,
            FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        if (handle.IsInvalid)
            throw new IOException($"Nepodarilo sa otvoriť {path} pre zápis (chyba {Marshal.GetLastWin32Error()}).");

        bool locked = false;
        try
        {
            for (int i = 0; i < 20; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (DeviceIoControl(handle, FsctlLockVolume, IntPtr.Zero, 0, IntPtr.Zero, 0,
                        out _, IntPtr.Zero))
                {
                    locked = true;
                    break;
                }
                Thread.Sleep(250);
            }

            if (!locked)
                throw new IOException("Volume sa nepodarilo uzamknúť. Zatvorte programy, ktoré ho používajú.");

            if (!DeviceIoControl(handle, FsctlDismountVolume, IntPtr.Zero, 0, IntPtr.Zero, 0,
                    out _, IntPtr.Zero))
                throw new IOException("Volume sa nepodarilo odpojiť (chyba " + Marshal.GetLastWin32Error() + ").");

            if (!SetFilePointerEx(handle, offset, out _, 0))
                throw new IOException("Nepodarilo sa nastaviť pozíciu zápisu (chyba " + Marshal.GetLastWin32Error() + ").");

            const int chunkSize = 1 << 20;
            int pos = 0;
            while (pos < data.Length)
            {
                ct.ThrowIfCancellationRequested();
                int count = Math.Min(chunkSize, data.Length - pos);
                var chunk = new byte[count];
                Buffer.BlockCopy(data, pos, chunk, 0, count);

                if (!WriteFile(handle, chunk, chunk.Length, out int written, IntPtr.Zero) ||
                    written != chunk.Length)
                    throw new IOException("Zápis zlyhal (chyba " + Marshal.GetLastWin32Error() + ").");

                pos += count;
            }

            FlushFileBuffers(handle);
            log($"Zápis dokončený: {data.Length:N0} B.");
        }
        finally
        {
            if (locked)
            {
                try
                {
                    DeviceIoControl(handle, FsctlUnlockVolume, IntPtr.Zero, 0, IntPtr.Zero, 0,
                        out _, IntPtr.Zero);
                }
                catch { }
            }
        }
    }
}
