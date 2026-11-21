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
    public static string LastAddress = "";
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint SmartRcvDriveData = 0x0007C088;
    private const uint IoctlScsiPassThroughDirect = 0x0004D014;
    private const uint IoctlScsiGetAddress = 0x00041018;
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
        => ReadAtaScsi(diskNumber, 0xB0, 0xD0, 0x01, 0x01, 0x4F, 0xC2, requireSmart: true);

    /// <summary>Prečíta ATA IDENTIFY DEVICE (skutočný model a sériové číslo).</summary>
    public static (string Serial, string Model)? ReadIdentify(int diskNumber)
    {
        var page = ReadAtaScsi(diskNumber, 0xEC, 0x00, 0x01, 0x00, 0x00, 0x00,
            requireSmart: false);
        if (page is null) return null;
        var (serial, model) = ParseIdentify(page);
        if (serial.Length == 0 && model.Length == 0) return null;
        return (serial, model);
    }

    private static byte[]? ReadAtaScsi(int diskNumber, byte command, byte features,
        byte count, byte lbaLow, byte lbaMid, byte lbaHigh, bool requireSmart)
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

            byte pathId = 0;
            byte targetId = 0;
            byte lun = 0;
            IntPtr address = Marshal.AllocHGlobal(8);
            try
            {
                for (int i = 0; i < 8; i++) Marshal.WriteByte(address, i, 0);
                if (DeviceIoControlPtr(handle, IoctlScsiGetAddress,
                        IntPtr.Zero, 0, address, 8, out int addressReturned, IntPtr.Zero) &&
                    addressReturned >= 8)
                {
                    pathId = Marshal.ReadByte(address, 5);
                    targetId = Marshal.ReadByte(address, 6);
                    lun = Marshal.ReadByte(address, 7);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(address);
            }
            LastAddress = $"{pathId}/{targetId}/{lun}";

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
                cdb16[4] = features;
                cdb16[6] = count;
                cdb16[8] = lbaLow;
                cdb16[10] = lbaMid;
                cdb16[12] = lbaHigh;
                cdb16[13] = 0xA0;
                cdb16[14] = command;
                variants.Add((cdb16, 16));

                var cdb12 = new byte[16];
                cdb12[0] = 0xA1;
                cdb12[1] = 0x08;
                cdb12[2] = direction;
                cdb12[3] = features;
                cdb12[4] = count;
                cdb12[5] = lbaLow;
                cdb12[6] = lbaMid;
                cdb12[7] = lbaHigh;
                cdb12[8] = 0xA0;
                cdb12[9] = command;
                variants.Add((cdb12, 12));
            }

            foreach (var variant in variants)
            {
                var packet = new ScsiPassThroughDirect
                {
                    Length = (ushort)structSize,
                    PathId = pathId,
                    TargetId = targetId,
                    Lun = lun,
                    CdbLength = variant.CdbLength,
                    SenseInfoLength = (byte)senseSize,
                    DataIn = 1,
                    DataTransferLength = PageSize,
                    TimeOutValue = 5,
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

                if (requireSmart)
                {
                    var attributes = ParseAttributes(page);
                    if (attributes.Count >= 3 &&
                        attributes.Any(a => a.Id is 5 or 9 or 194 or 197 or 198))
                        return page;
                    errors.Add($"empty {variant.Cdb[0]:X2}/{variant.Cdb[2]:X2} " +
                               $"status={back.ScsiStatus} attrs={attributes.Count}");
                }
                else
                {
                    var (serial, model) = ParseIdentify(page);
                    if (serial.Length > 0 || model.Length > 0)
                        return page;
                    errors.Add($"empty-identify {variant.Cdb[0]:X2}/{variant.Cdb[2]:X2} " +
                               $"status={back.ScsiStatus}");
                }
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
        if (sense.Length < 8) return false;

        // Fixed format: 70 00 01 ... (sense key = RECOVERED ERROR, ASC/ASCQ 00/1D).
        if (sense[0] == 0x70)
            return (sense[2] & 0x0F) == 0x01;

        if (sense[0] != 0x72) return false;
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

    private static (string Serial, string Model) ParseIdentify(byte[] page)
    {
        if (page.Length < 94) return ("", "");
        return (AtaString(page, 20, 10), AtaString(page, 54, 20));
    }

    private static string AtaString(byte[] page, int offset, int words)
    {
        var chars = new char[words * 2];
        for (int i = 0; i < words; i++)
        {
            chars[i * 2] = (char)page[offset + i * 2 + 1];
            chars[i * 2 + 1] = (char)page[offset + i * 2];
        }
        var text = new string(chars)
            .Where(c => c is >= (char)0x20 and < (char)0x7F)
            .ToArray();
        return new string(text).Trim();
    }

    public static List<SmartAttribute> ParseAttributes(byte[] page)
    {        var attributes = new List<SmartAttribute>();
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
