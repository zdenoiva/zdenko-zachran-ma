using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NtfsRescue.Core;

/// <summary>NVMe SMART/Health log page (log 0x02) načítaný cez DeviceIoControl.</summary>
public sealed class NvmeSmartLog
{
    public bool Available;
    public byte CriticalWarning;
    public int TemperatureC;
    public int AvailableSpare;
    public int SpareThreshold;
    public int PercentageUsed;
    public ulong DataUnitsRead;
    public ulong DataUnitsWritten;
    public ulong HostReadCommands;
    public ulong HostWriteCommands;
    public ulong ControllerBusyMinutes;
    public ulong PowerCycles;
    public ulong PowerOnHours;
    public ulong UnsafeShutdowns;
    public ulong MediaErrors;
    public ulong ErrorLogEntries;
    public int WarningTempMinutes;
    public int CriticalTempMinutes;
    public int Sensor1C;
    public int Sensor2C;

    /// <summary>NVMe počíta dátové jednotky po 1 000 × 512 B.</summary>
    public ulong ReadBytes => DataUnitsRead * 512_000UL;
    public ulong WrittenBytes => DataUnitsWritten * 512_000UL;
}

public static class NvmeSmart
{
    public static string LastError = "";
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint IoctlStorageProtocolCommand = 0x002D1408;
    private const int DeviceProtocolSpecificProperty = 50;
    private const int AdapterProtocolSpecificProperty = 49;
    private const int StandardQuery = 0;
    private const int ProtocolNvme = 3;
    private const int NvmeDataTypeLogPage = 2;
    private const int SmartHealthLogId = 2;
    private const int PageSize = 512;

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery
    {
        public int PropertyId;
        public int QueryType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 40)]
        public byte[] AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProtocolSpecificData
    {
        public int ProtocolType;
        public int DataType;
        public int ProtocolDataRequestValue;
        public int ProtocolDataRequestSubValue;
        public int ProtocolDataOffset;
        public int ProtocolDataLength;
        public int FixedProtocolReturnData;
        public int ProtocolDataRequestSubValue2;
        public int ProtocolDataRequestSubValue3;
        public int ProtocolDataRequestSubValue4;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProtocolCommand
    {
        public uint Version;
        public uint Length;
        public uint ProtocolType;
        public uint Flags;
        public uint ReturnStatus;
        public uint ErrorCode;
        public uint CommandLength;
        public uint ErrorInfoLength;
        public uint DataToDeviceTransferLength;
        public uint DataFromDeviceTransferLength;
        public uint TimeOutValue;
        public uint ErrorInfoOffset;
        public uint DataToDeviceBufferOffset;
        public uint DataFromDeviceBufferOffset;
        public uint CommandSpecific;
        public uint Reserved0;
        public uint FixedProtocolReturnData;
        public uint Reserved1;
        public uint Reserved2;
        public uint Reserved3;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public byte[] Command;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode,
        IntPtr inBuffer, int inBufferSize, IntPtr outBuffer, int outBufferSize,
        out int bytesReturned, IntPtr overlapped);

    /// <summary>Vráti null, ak disk nie je NVMe alebo log nie je dostupný.</summary>
    public static NvmeSmartLog? Query(int diskNumber)
    {
        SafeFileHandle? handle = null;
        IntPtr buffer = IntPtr.Zero;
        try
        {
            handle = CreateFileW($@"\\.\PhysicalDrive{diskNumber}", GenericRead | GenericWrite,
                FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                LastError = "CreateFile " + Marshal.GetLastWin32Error();
                return null;
            }

            var protocol = new ProtocolSpecificData
            {
                ProtocolType = ProtocolNvme,
                DataType = NvmeDataTypeLogPage,
                ProtocolDataRequestValue = SmartHealthLogId,
                ProtocolDataOffset = Marshal.SizeOf<ProtocolSpecificData>(),
                ProtocolDataLength = PageSize
            };

            // Windows odporúča jeden buffer pre vstup aj výstup.
            int bufferSize = 8 + Marshal.SizeOf<ProtocolSpecificData>() + PageSize;
            buffer = Marshal.AllocHGlobal(bufferSize);

            var errors = new List<string>();
            byte[]? page = null;
            // Radič môže podporovať len jednu z týchto vlastností.
            foreach (int propertyId in new[] { DeviceProtocolSpecificProperty, AdapterProtocolSpecificProperty })
            {
                for (int i = 0; i < bufferSize; i++) Marshal.WriteByte(buffer, i, 0);
                var query = new StoragePropertyQuery
                {
                    PropertyId = propertyId,
                    QueryType = StandardQuery,
                    AdditionalParameters = new byte[40]
                };
                Marshal.StructureToPtr(query, buffer, false);
                Marshal.StructureToPtr(protocol, buffer + 8, false);

                if (!DeviceIoControl(handle, IoctlStorageQueryProperty, buffer, bufferSize,
                        buffer, bufferSize, out int returned, IntPtr.Zero) || returned <= 0)
                {
                    errors.Add($"{propertyId}:{Marshal.GetLastWin32Error()}/{returned}");
                    continue;
                }

                var raw = new byte[returned];
                Marshal.Copy(buffer, raw, 0, returned);
                page = ExtractPage(raw);
                if (page is not null) break;
                errors.Add($"{propertyId}:no-page/{returned}/{Convert.ToHexString(raw.AsSpan(0, Math.Min(raw.Length, 48)))}");
            }

            LastError = string.Join("; ", errors);
            if (page is null)
                page = QueryPassThrough(handle);
            if (page is null) return null;

            var log = Parse(page);
            if (!Plausible(log)) return null;
            log.Available = true;
            return log;
        }
        catch
        {
            LastError = "výnimka";
            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            try { handle?.Dispose(); } catch { }
        }
    }

    /// <summary>Záložná cesta cez NVMe pass-through (IOCTL_STORAGE_PROTOCOL_COMMAND).</summary>
    private static byte[]? QueryPassThrough(SafeFileHandle handle)
    {
        const int headerSize = 80;
        const int commandSize = 64;
        int structSize = headerSize + commandSize;
        int bufferSize = structSize + PageSize;
        IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            for (int i = 0; i < bufferSize; i++) Marshal.WriteByte(buffer, i, 0);

            var command = new ProtocolCommand
            {
                Version = 1,
                Length = (uint)structSize,
                ProtocolType = ProtocolNvme,
                CommandLength = commandSize,
                DataFromDeviceTransferLength = PageSize,
                TimeOutValue = 10,
                DataFromDeviceBufferOffset = (uint)structSize,
                CommandSpecific = 1, // admin command
                Command = new byte[commandSize]
            };
            command.Command[0] = 0x02;                    // Get Log Page
            command.Command[4] = 0xFF;                    // NSID = all
            command.Command[5] = 0xFF;
            command.Command[6] = 0xFF;
            command.Command[7] = 0xFF;
            command.Command[10] = SmartHealthLogId;       // LID
            command.Command[12] = 0x7F;                   // NUMDL = 127 (512 B)

            Marshal.StructureToPtr(command, buffer, false);
            if (!DeviceIoControl(handle, IoctlStorageProtocolCommand, buffer, bufferSize,
                    buffer, bufferSize, out int returned, IntPtr.Zero) || returned < bufferSize)
            {
                LastError = "passthrough " + Marshal.GetLastWin32Error() + "/" + returned;
                return null;
            }

            var result = new byte[PageSize];
            Marshal.Copy(buffer + structSize, result, 0, PageSize);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Výstup: descriptor (8 B) + protocol data (40 B) + log page.</summary>
    private static byte[]? ExtractPage(byte[] raw)
    {
        if (raw.Length < 48 + PageSize) return null;
        int version = BitConverter.ToInt32(raw, 0);
        int size = BitConverter.ToInt32(raw, 4);
        int protocolDataOffset = BitConverter.ToInt32(raw, 8 + 16);
        if (version < 8 || size < 8) return null;
        if (protocolDataOffset < 8) return null;
        int payload = 8 + protocolDataOffset;
        if (payload + PageSize > raw.Length) return null;

        var page = new byte[PageSize];
        Array.Copy(raw, payload, page, 0, PageSize);
        return page;
    }

    private static NvmeSmartLog Parse(byte[] b)
    {
        var log = new NvmeSmartLog
        {
            CriticalWarning = b[0],
            TemperatureC = U16(b, 1),
            AvailableSpare = b[3],
            SpareThreshold = b[4],
            PercentageUsed = b[5],
            DataUnitsRead = U128(b, 32),
            DataUnitsWritten = U128(b, 48),
            HostReadCommands = U128(b, 64),
            HostWriteCommands = U128(b, 80),
            ControllerBusyMinutes = U128(b, 96),
            PowerCycles = U128(b, 112),
            PowerOnHours = U128(b, 128),
            UnsafeShutdowns = U128(b, 144),
            MediaErrors = U128(b, 160),
            ErrorLogEntries = U128(b, 176),
            WarningTempMinutes = (int)U128(b, 192),
            CriticalTempMinutes = (int)U128(b, 196),
            Sensor1C = U16(b, 200),
            Sensor2C = U16(b, 202)
        };

        log.TemperatureC = KelvinToCelsius(log.TemperatureC);
        log.Sensor1C = KelvinToCelsius(log.Sensor1C);
        log.Sensor2C = KelvinToCelsius(log.Sensor2C);
        return log;
    }

    private static int U16(byte[] b, int offset)
        => b[offset] | (b[offset + 1] << 8);

    private static ulong U128(byte[] b, int offset)
    {
        ulong value = 0;
        for (int i = 7; i >= 0; i--)
            value = (value << 8) | b[offset + i];
        return value;
    }

    private static int KelvinToCelsius(int value)
        => value >= 273 && value <= 400 ? value - 273 : value;

    private static bool Plausible(NvmeSmartLog log)
    {
        if (log.TemperatureC is < -20 or > 150) return false;
        if (log.AvailableSpare is < 0 or > 100) return false;
        if (log.SpareThreshold is < 0 or > 100) return false;
        if (log.PercentageUsed is < 0 or > 100) return false;
        if (log.PowerOnHours > 10_000_000) return false;
        if (log.PowerCycles > 100_000_000) return false;
        return true;
    }
}
