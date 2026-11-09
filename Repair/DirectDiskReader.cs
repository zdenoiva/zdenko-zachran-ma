using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NtfsRescue.Core;

namespace NtfsRescue.Repair;

/// <summary>Čítanie fyzického disku bez vyrovnávacej pamäte Windows (ak to zariadenie podporuje).</summary>
public sealed class DirectDiskReader : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint NoBuffering = 0x20000000;
    private const uint SequentialScan = 0x08000000;
    private const int MaxRead = 4 * 1024 * 1024;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFilePointerEx(SafeFileHandle file, long distance,
        out long newPointer, uint method);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(SafeFileHandle file, IntPtr buffer, int length,
        out int read, IntPtr overlapped);

    private readonly SafeFileHandle? _handle;
    private readonly RawDisk? _fallback;
    private readonly IntPtr _allocation;
    private readonly IntPtr _aligned;

    public bool Direct { get; }

    public DirectDiskReader(int diskNumber)
    {
        var path = $@"\\.\PhysicalDrive{diskNumber}";
        try
        {
            var handle = CreateFileW(path, GenericRead, FileShareRead | FileShareWrite,
                IntPtr.Zero, OpenExisting, NoBuffering | SequentialScan, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                handle.Dispose();
                throw new IOException("Priame čítanie nie je dostupné.");
            }

            var allocation = Marshal.AllocHGlobal(MaxRead + 4096);
            long address = allocation.ToInt64();
            var aligned = new IntPtr((address + 4095) & ~4095L);

            // Otestujeme, či radič no-buffering naozaj podporuje.
            if (!SetFilePointerEx(handle, 0, out _, 0) ||
                !ReadFile(handle, aligned, 512, out int read, IntPtr.Zero) ||
                read != 512)
            {
                handle.Dispose();
                Marshal.FreeHGlobal(allocation);
                throw new IOException("Priame čítanie nie je dostupné.");
            }

            _allocation = allocation;
            _aligned = aligned;
            _handle = handle;
            Direct = true;
        }
        catch
        {
            _fallback = new RawDisk(diskNumber, 512, 0);
            Direct = false;
        }
    }

    public void ReadAt(long offset, byte[] managedBuffer, int length)
    {
        if (length <= 0) return;
        if (_handle is not null && offset % 512 == 0 && length % 512 == 0)
        {
            try
            {
                if (!SetFilePointerEx(_handle, offset, out _, 0))
                    throw new IOException("Nepodarilo sa nastaviť pozíciu čítania.");
                if (!ReadFile(_handle, _aligned, length, out int read, IntPtr.Zero) || read != length)
                    throw new IOException("Čítanie disku zlyhalo (chyba " + Marshal.GetLastWin32Error() + ").");
                return;
            }
            catch
            {
                // Niektoré radiče odmietnu no-buffering; skúsime bežné čítanie.
            }
        }

        if (_fallback is not null)
            _fallback.ReadAt(offset, managedBuffer, length);
        else
            throw new IOException("Sektor sa nepodarilo prečítať.");
    }

    public void Dispose()
    {
        try { _handle?.Dispose(); } catch { }
        try { _fallback?.Dispose(); } catch { }
        if (_allocation != IntPtr.Zero)
        {
            try { Marshal.FreeHGlobal(_allocation); } catch { }
        }
    }
}
