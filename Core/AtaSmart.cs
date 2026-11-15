using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NtfsRescue.Core;

/// <summary>
/// Priame ATA S.M.A.R.T. čítanie cez SMART_RCV_DRIVE_DATA.
/// Funguje aj na diskoch za USB-SATA mostíkmi, kde WMI SMART nie je dostupné.
/// </summary>
public static class AtaSmart
{
    public static string LastError = "";
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint SmartRcvDriveData = 0x0007C088;
    private const uint IoctlScsiPassThroughDirect = 0x0004D014;
    private const int PageSize = 512;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode,
        byte[] inBuffer, int inBufferSize, byte[] outBuffer, int outBufferSize,
        out int bytesReturned, IntPtr overlapped);

    /// <summary>Vráti 512-bajtovú SMART data page alebo null.</summary>
    public static byte[]? ReadPage(int diskNumber)
    {
        var page = ReadPageLegacy(diskNumber);
        if (page is not null) return page;
        return ReadPageScsi(diskNumber);
    }

    private static byte[]? ReadPageLegacy(int diskNumber)
    {
        SafeFileHandle? handle = null;
        try
        {
            handle = CreateFileW($@"\\.\PhysicalDrive{diskNumber}",
                GenericRead | GenericWrite, FileShareRead | FileShareWrite,
                IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid) return null;

            var input = new byte[32];
            WriteInt(input, 0, PageSize);       // cBufferSize
            input[4] = 0xD0;                    // bFeaturesReg = SMART
            input[5] = 0x01;                    // bSectorCountReg
            input[6] = 0x01;                    // bSectorNumberReg
            input[7] = 0x4F;                    // bCylLowReg
            input[8] = 0xC2;                    // bCylHighReg
            input[9] = 0xA0;                    // bDriveHeadReg
            input[10] = 0xB0;                   // bCommandReg = SMART
            input[11] = 0x00;                   // bDriveNumber
            // bReserved[3] and dwReserved[4] stay zero.

            var output = new byte[16 + PageSize];
            if (!DeviceIoControl(handle, SmartRcvDriveData, input, input.Length,
                    output, output.Length, out int returned, IntPtr.Zero) ||
                returned < 16 + PageSize)
                return null;

            // SENDCMDOUTPARAMS: cBufferSize (4), DriverStatus (12), then data.
            if (output[4] != 0) return null;    // bDriverError

            var page = new byte[PageSize];
            Array.Copy(output, 16, page, 0, PageSize);
            return page;
        }
        catch
        {
            return null;
        }
        finally
        {
            try { handle?.Dispose(); } catch { }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ScsiPassThroughDirect
    {
        public ushort Length;
        public byte ScsiStatus;
        public byte PathId;
        public byte TargetId;
        public byte Lun;
        public byte CdbLength;
        public byte SenseInfoLength;
        public byte DataIn;
        public uint DataTransferLength;
        public uint TimeOutValue;
        public IntPtr DataBuffer;
        public uint SenseInfoOffset;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] Cdb;
    }

    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControlPtr(SafeFileHandle device, uint ioControlCode,
        IntPtr inBuffer, int inBufferSize, IntPtr outBuffer, int outBufferSize,
        out int bytesReturned, IntPtr overlapped);

    private static byte[]? ReadPageScsi(int diskNumber)
    {
        SafeFileHandle? handle = null;
        IntPtr buffer = IntPtr.Zero;
        IntPtr data = IntPtr.Zero;
        var errors = new List<string>();
        try
        {
            handle = CreateFileW($@"\\.\PhysicalDrive{diskNumber}",
                GenericRead | GenericWrite, FileShareRead | FileShareWrite,
                IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                LastError = "scsi open " + Marshal.GetLastWin32Error();
                return null;
            }

            int structSize = Marshal.SizeOf<ScsiPassThroughDirect>();
            int senseSize = 32;
            data = Marshal.AllocHGlobal(PageSize);
            buffer = Marshal.AllocHGlobal(structSize + senseSize);

            var variants = new List<(byte[] Cdb, byte CdbLength)>();
            foreach (byte direction in new byte[] { 0x2E, 0x3E })
            {
                var cdb16 = new byte[16];
                cdb16[0] = 0x85;
                cdb16[1] = 0x08;
                cdb16[2] = direction;
                cdb16[4] = 0xD0;
                cdb16[6] = 0x01;
                cdb16[8] = 0x01;
                cdb16[10] = 0x4F;
                cdb16[12] = 0xC2;
                cdb16[13] = 0xA0;
                cdb16[14] = 0xB0;
                variants.Add((cdb16, 16));

                var cdb12 = new byte[16];
                cdb12[0] = 0xA1;
                cdb12[1] = 0x08;
                cdb12[2] = direction;
                cdb12[3] = 0xD0;
                cdb12[4] = 0x01;
                cdb12[5] = 0x01;
                cdb12[6] = 0x4F;
                cdb12[7] = 0xC2;
                cdb12[8] = 0xA0;
                cdb12[9] = 0xB0;
                variants.Add((cdb12, 12));
            }

            foreach (var variant in variants)
            {
                var packet = new ScsiPassThroughDirect
                {
                    Length = (ushort)structSize,
                    CdbLength = variant.CdbLength,
                    SenseInfoLength = (byte)senseSize,
                    DataIn = 1,
                    DataTransferLength = PageSize,
                    TimeOutValue = 10,
                    DataBuffer = data,
                    SenseInfoOffset = (uint)structSize,
                    Cdb = variant.Cdb
                };

                for (int i = 0; i < structSize + senseSize; i++) Marshal.WriteByte(buffer, i, 0);
                Marshal.StructureToPtr(packet, buffer, false);

                if (!DeviceIoControlPtr(handle, IoctlScsiPassThroughDirect,
                        buffer, structSize + senseSize, buffer, structSize + senseSize,
                        out int returned, IntPtr.Zero) || returned <= 0)
                {
                    errors.Add($"ioctl {variant.Cdb[0]:X2}/{variant.Cdb[2]:X2} " +
                               $"{Marshal.GetLastWin32Error()}/{returned}");
                    continue;
                }

                var back = Marshal.PtrToStructure<ScsiPassThroughDirect>(buffer);
                var sense = new byte[senseSize];
                Marshal.Copy(buffer + structSize, sense, 0, senseSize);
                if (back.ScsiStatus != 0 && !AtaRecoveredOk(sense))
                {
                    errors.Add($"scsi {variant.Cdb[0]:X2}/{variant.Cdb[2]:X2} " +
                               $"status={back.ScsiStatus} sense={Convert.ToHexString(sense)}");
                    continue;
                }

                var page = new byte[PageSize];
                Marshal.Copy(data, page, 0, PageSize);
                var attributes = ParseAttributes(page);
                if (attributes.Count >= 3 &&
                    attributes.Any(a => a.Id is 5 or 9 or 194 or 197 or 198))
                    return page;
                errors.Add($"empty {variant.Cdb[0]:X2}/{variant.Cdb[2]:X2} " +
                           $"status={back.ScsiStatus} attrs={attributes.Count}");
            }

            LastError = string.Join("; ", errors);
            return null;
        }
        catch (Exception ex)
        {
            LastError = "scsi " + ex.Message;
            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (data != IntPtr.Zero) Marshal.FreeHGlobal(data);
            try { handle?.Dispose(); } catch { }
        }
    }

    private static bool AtaRecoveredOk(byte[] sense)
    {
        if (sense.Length < 8 || sense[0] != 0x72) return false;
        if ((sense[1] & 0x0F) != 0x01) return false; // RECOVERED ERROR

        int length = sense[7];
        int offset = 8;
        int end = Math.Min(sense.Length, 8 + length);
        while (offset + 2 <= end)
        {
            int type = sense[offset];
            int descriptorLength = sense[offset + 1];
            if (type == 0x09 && descriptorLength >= 12 &&
                offset + 2 + 12 <= sense.Length)
            {
                byte status = sense[offset + 2 + 11];
                return (status & 0x01) == 0;
            }
            offset += 2 + descriptorLength;
        }
        return false;
    }

    public static List<SmartAttribute> ParseAttributes(byte[] page)
    {
        var attributes = new List<SmartAttribute>();
        if (page.Length < 362) return attributes;

        for (int i = 0; i < 30; i++)
        {
            int offset = 2 + i * 12;
            if (offset + 11 >= page.Length) break;
            int id = page[offset];
            if (id <= 0) continue;

            long raw = 0;
            for (int k = 0; k < 6; k++)
                raw |= (long)page[offset + 5 + k] << (8 * k);

            attributes.Add(new SmartAttribute
            {
                Id = id,
                Value = page[offset + 3],
                Worst = page[offset + 4],
                Raw = raw
            });
        }
        return attributes;
    }

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }
}
