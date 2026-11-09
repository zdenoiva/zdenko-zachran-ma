using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NtfsRescue.Util;

namespace NtfsRescue.Repair;

/// <summary>
/// Obnova primárneho boot sectoru zo záložného. Zapisuje len prvých 512 bajtov
/// partície, a to až po uzamknutí a odpojení volume.
/// </summary>
public static class BootSectorRestorer
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

    public static void Restore(char letter, byte[] backupBootSector, Action<string> log, CancellationToken ct)
    {
        if (backupBootSector.Length < 512)
            throw new ArgumentException("Záložný boot sector musí mať aspoň 512 bajtov.");

        var parsed = Core.NtfsBootSector.Parse(backupBootSector);
        if (!parsed.IsValid)
            throw new InvalidDataException("Záložný boot sector nie je platný NTFS boot sector: " + parsed.Error);

        var path = $@"\\.\{char.ToUpperInvariant(letter)}:";
        log($"Otváram {path} pre zápis...");

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
                throw new IOException(
                    "Volume sa nepodarilo uzamknúť. Zatvorte všetky okná a programy, ktoré ho používajú, a skúste znova.");

            if (!DeviceIoControl(handle, FsctlDismountVolume, IntPtr.Zero, 0, IntPtr.Zero, 0,
                    out _, IntPtr.Zero))
                throw new IOException("Volume sa nepodarilo odpojiť (chyba " + Marshal.GetLastWin32Error() + ").");

            if (!SetFilePointerEx(handle, 0, out _, 0))
                throw new IOException("Nepodarilo sa nastaviť pozíciu zápisu (chyba " + Marshal.GetLastWin32Error() + ").");

            var sector = new byte[512];
            Buffer.BlockCopy(backupBootSector, 0, sector, 0, 512);

            if (!WriteFile(handle, sector, sector.Length, out int written, IntPtr.Zero) || written != sector.Length)
                throw new IOException("Zápis boot sectoru zlyhal (chyba " + Marshal.GetLastWin32Error() + ").");

            FlushFileBuffers(handle);
            log("Boot sector bol obnovený.");
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
