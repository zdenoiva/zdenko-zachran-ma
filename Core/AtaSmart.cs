using System.Runtime.InteropServices;
using System.Text;
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
    public static string LastProtocol = "";
    public static string LastWakeUp = "";
    public static string LastBridge = "";
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint SmartRcvDriveData = 0x0007C088;
    private const uint IoctlScsiPassThroughDirect = 0x0004D014;
    private const uint IoctlScsiPassThrough = 0x0004D004;
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(SafeFileHandle file, byte[] buffer, int bytesToRead,
        out int bytesRead, IntPtr overlapped);

    /// <summary>
    /// Prečíta sektor 0, čím sa 3,5" disk roztočí. CrystalDiskInfo to robí
    /// pred SMART príkazmi; bez toho mostík hlási "medium not present".
    /// </summary>
    public static string WakeUpDisk(int diskNumber)
    {
        LastWakeUp = "";
        if (TryReadSector(diskNumber, out int error))
        {
            LastWakeUp = "OK (512 B)";
            return LastWakeUp;
        }

        // ERROR_NOT_READY (21): médium je zastavené. Skúsime ho naštartovať.
        if (error == 21 && StartUnit(diskNumber))
        {
            if (TryReadSector(diskNumber, out error))
            {
                LastWakeUp = "OK po START UNIT (512 B)";
                return LastWakeUp;
            }
            LastWakeUp = $"read {error} po START UNIT";
            return LastWakeUp;
        }

        LastWakeUp = "read " + error;
        return LastWakeUp;
    }

    private static bool TryReadSector(int diskNumber, out int error)
    {
        error = 0;
        SafeFileHandle? handle = null;
        try
        {
            handle = CreateFileW($@"\\.\PhysicalDrive{diskNumber}",
                GenericRead, FileShareRead | FileShareWrite,
                IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }

            var buffer = new byte[512];
            if (!ReadFile(handle, buffer, buffer.Length, out _, IntPtr.Zero))
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { handle?.Dispose(); } catch { }
        }
    }

    /// <summary>SCSI START STOP UNIT (0x1B) so Start=1 — naštartuje zastavené médium.</summary>
    private static bool StartUnit(int diskNumber)
    {
        if (!EnterScsi()) return false;
        SafeFileHandle? handle = null;
        try
        {
            handle = CreateFileW($@"\\.\PhysicalDrive{diskNumber}",
                GenericRead | GenericWrite, FileShareRead | FileShareWrite,
                IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid) return false;
            if (!TryGetAddress(handle, out byte pathId, out byte targetId, out byte lun))
                return false;

            var cdb = new byte[] { 0x1B, 0x00, 0x00, 0x00, 0x01, 0x00 };
            SendDirect(handle, pathId, targetId, lun, cdb, 6, 0, 5,
                out byte status, out _, out int win32, out _);
            return win32 == 0 && status == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { handle?.Dispose(); } catch { }
            LeaveScsi();
        }
    }

    /// <summary>Vráti 512-bajtovú SMART data page alebo null.</summary>
    public static byte[]? ReadPage(int diskNumber)
        => ReadPage(diskNumber, 15000);

    /// <summary>Vráti SMART page, pričom skúšanie variantov zastaví po budgetMs.</summary>
    public static byte[]? ReadPage(int diskNumber, int budgetMs, string expectedModel = "")
    {
        if (ScsiDisabled) return null;
        var wake = WakeUpDisk(diskNumber);
        // Box po nečinnosti vypne port a čítanie vráti "device not ready" (21).
        // Reset USB mostíka ho znovu sprístupní; počas operácie nad diskom nie.
        if (wake.StartsWith("read 21", StringComparison.Ordinal) &&
            UsbBridge.AutoReset && !AppState.Busy)
        {
            if (UsbBridge.Reset(diskNumber))
            {
                Thread.Sleep(5000);
                WakeUpDisk(diskNumber);
            }
        }
        var page = ReadPageLegacy(diskNumber);
        if (page is not null) return page;
        return ReadAtaScsi(diskNumber, 0xB0, 0xD0, 0x01, 0x01, 0x4F, 0xC2,
            requireSmart: true, budgetMs, expectedModel);
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
    private struct ScsiPassThrough
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
        // V 64-bit Windows je DataBufferOffset typu ULONG_PTR, nie ULONG.
        // Pri ULONG vychádza veľkosť štruktúry 44 namiesto 56 a ovládač
        // vráti ERROR_REVISION_MISMATCH (1306).
        public UIntPtr DataBufferOffset;
        public uint SenseInfoOffset;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] Cdb;
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
    public static (string Serial, string Model)? ReadIdentify(int diskNumber, int budgetMs = 6000,
        string expectedModel = "")
    {
        if (ScsiDisabled) return null;
        WakeUpDisk(diskNumber);
        var page = ReadAtaScsi(diskNumber, 0xEC, 0x00, 0x01, 0x00, 0x00, 0x00,
            requireSmart: false, budgetMs, expectedModel);
        if (page is null) return null;
        var (serial, model) = ParseIdentify(page);
        if (serial.Length == 0 && model.Length == 0) return null;
        return (serial, model);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, object> Gates = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> Preferred = new();
    private static readonly Mutex ScsiMutex = new(false, @"Local\ZdenkoZachranMa-scsi");

    /// <summary>Núdzový vypínač: ZDENKO_NO_SCSI=1 vypne všetky priame SCSI/ATA príkazy.</summary>
    public static bool ScsiDisabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ZDENKO_NO_SCSI"));

    /// <summary>ASM1352 vendor príkazy sú len na explicitné zapnutie — firmvér ich zablokuje.</summary>
    public static bool AsmEnabled =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ZDENKO_ASM1352"));

    private static bool EnterScsi(int timeoutMs = 10000)
    {
        if (ScsiDisabled) return false;
        try { return ScsiMutex.WaitOne(timeoutMs); }
        catch (AbandonedMutexException) { return true; }
        catch { return false; }
    }

    private static void LeaveScsi()
    {
        try { ScsiMutex.ReleaseMutex(); } catch { }
    }

    private static object Gate(int diskNumber)
        => Gates.GetOrAdd(diskNumber, _ => new object());

    /// <summary>Poradie CDB variantov pre daný disk a príkaz.</summary>
    private static List<(string Name, byte[] Cdb, byte CdbLength)> BuildVariants(
        int diskNumber, byte command, byte features, byte count,
        byte lbaLow, byte lbaMid, byte lbaHigh, byte lun, bool preferAsm,
        bool includeAsm = false)
    {
        var list = new List<(string Name, byte[] Cdb, byte CdbLength)>();

        void Add12(string name, byte opcode, byte protocol, byte flags)
        {
            var cdb = new byte[16];
            cdb[0] = opcode;
            cdb[1] = protocol;
            cdb[2] = flags;
            cdb[3] = features;
            cdb[4] = count;
            cdb[5] = lbaLow;
            cdb[6] = lbaMid;
            cdb[7] = lbaHigh;
            cdb[8] = 0xA0;
            cdb[9] = command;
            list.Add((name, cdb, 12));
        }

        void Add16(string name, byte opcode, byte protocol, byte flags)
        {
            var cdb = new byte[16];
            cdb[0] = opcode;
            cdb[1] = protocol;
            cdb[2] = flags;
            cdb[3] = 0x00;      // FEATURES (15:8)
            cdb[4] = features;  // FEATURES (7:0)
            cdb[5] = 0x00;      // SECTOR_COUNT (15:8)
            cdb[6] = count;     // SECTOR_COUNT (7:0)
            cdb[7] = 0x00;      // LBA_LOW (15:8)
            cdb[8] = lbaLow;    // LBA_LOW (7:0)
            cdb[9] = 0x00;      // LBA_MID (15:8)
            cdb[10] = lbaMid;   // LBA_MID (7:0)
            cdb[11] = 0x00;     // LBA_HIGH (15:8)
            cdb[12] = lbaHigh;  // LBA_HIGH (7:0)
            cdb[13] = 0xA0;     // DEVICE
            cdb[14] = command;  // COMMAND
            list.Add((name, cdb, 16));
        }

        // 1. Štandardné SAT-12 musí byť vždy prvé. Na tomto mostíku funguje,
        //    kým ASM príkaz ho zablokuje.
        Add12("SAT12", 0xA1, 0x08, 0x0E);

        // 2. Bridge hlási ASM1352: rezervované hodnoty protokolu vyberajú
        //    SATA port 0 (0x0D) alebo port 1 (0x0E). Až po SAT12.
        if (includeAsm && preferAsm)
        {
            Add16("ASM16-0E-0E", 0x85, (byte)(0x0E << 1), 0x0E);
            Add16("ASM16-0D-0E", 0x85, (byte)(0x0D << 1), 0x0E);
            Add16("ASM16-0E-2E", 0x85, (byte)(0x0E << 1), 0x2E);
            Add16("ASM16-0D-2E", 0x85, (byte)(0x0D << 1), 0x2E);
            Add12("ASM12-0E-0E", 0xA1, (byte)(0x0E << 1), 0x0E);
            Add12("ASM12-0D-0E", 0xA1, (byte)(0x0D << 1), 0x0E);
            Add12("ASM12-0E-2E", 0xA1, (byte)(0x0E << 1), 0x2E);
            Add12("ASM12-0D-2E", 0xA1, (byte)(0x0D << 1), 0x2E);
        }

        // 3. Štandardné SAT-16 (AXAGON a väčšina USB mostíkov).
        Add16("SAT16-2E", 0x85, 0x08, 0x2E);
        Add16("SAT16-0E", 0x85, 0x08, 0x0E);

        if (includeAsm && !preferAsm)
        {
            Add12("ASM1352R-0D", 0xA1, (byte)(0x0D << 1), 0x0E);
            Add12("ASM1352R-0E", 0xA1, (byte)(0x0E << 1), 0x0E);
        }

        // JMicron USB mostíky (napr. JMS578/JMS561) používajú vendor CDB DF 10.
        var jmicron = new byte[16];
        jmicron[0] = 0xDF;
        jmicron[1] = 0x10;
        jmicron[2] = 0x00;
        jmicron[3] = 0x02;
        jmicron[4] = 0x00;
        jmicron[5] = command == 0xB0 ? (byte)0xD0 : (byte)0x00;
        jmicron[6] = 0x01;
        jmicron[7] = command == 0xB0 ? (byte)0x01 : (byte)0x00;
        jmicron[8] = command == 0xB0 ? (byte)0x4F : (byte)0x00;
        jmicron[9] = command == 0xB0 ? (byte)0xC2 : (byte)0x00;
        jmicron[10] = 0xA0;
        jmicron[11] = command;
        list.Add(("JMicron", jmicron, 12));

        // Sunplus USB mostíky.
        var sunplus = new byte[16];
        sunplus[0] = 0xF8;
        sunplus[1] = 0x00;
        sunplus[2] = 0x22;
        sunplus[3] = 0x10;
        sunplus[4] = 0x01;
        sunplus[5] = command == 0xB0 ? (byte)0xD0 : (byte)0x00;
        sunplus[6] = 0x01;
        sunplus[7] = 0x00;
        sunplus[8] = command == 0xB0 ? (byte)0x4F : (byte)0x00;
        sunplus[9] = command == 0xB0 ? (byte)0xC2 : (byte)0x00;
        sunplus[10] = 0xA0;
        sunplus[11] = command;
        list.Add(("Sunplus", sunplus, 12));

        // Cypress USB mostíky.
        var cypress = new byte[16];
        cypress[0] = 0x24;
        cypress[1] = 0x24;
        cypress[2] = 0x00;
        cypress[3] = 0xBE;
        cypress[4] = 0x01;
        cypress[5] = 0x00;
        cypress[6] = command == 0xB0 ? (byte)0xD0 : (byte)0x00;
        cypress[9] = command == 0xB0 ? (byte)0x4F : (byte)0x00;
        cypress[10] = command == 0xB0 ? (byte)0xC2 : (byte)0x00;
        cypress[11] = 0xA0;
        cypress[12] = command;
        list.Add(("Cypress", cypress, 16));

        // Prolific USB mostíky.
        var prolific = new byte[16];
        prolific[0] = 0xD8;
        prolific[1] = 0x15;
        prolific[2] = 0x00;
        prolific[3] = command == 0xB0 ? (byte)0xD0 : (byte)0x00;
        prolific[4] = 0x06;
        prolific[5] = 0x7B;
        prolific[6] = 0x00;
        prolific[7] = 0x00;
        prolific[8] = 0x02;
        prolific[9] = 0x00;
        prolific[10] = 0x01;
        prolific[11] = 0x00;
        prolific[12] = command == 0xB0 ? (byte)0x4F : (byte)0x00;
        prolific[13] = command == 0xB0 ? (byte)0xC2 : (byte)0x00;
        prolific[14] = 0xA0;
        prolific[15] = command;
        list.Add(("Prolific", prolific, 16));

        // Ak už v tejto relácii niektorý variant uspel, skúsime ho ako prvý.
        if (Preferred.TryGetValue(diskNumber, out string? name))
        {
            int index = list.FindIndex(v => v.Name == name);
            if (index > 0)
            {
                var preferred = list[index];
                list.RemoveAt(index);
                list.Insert(0, preferred);
            }
        }

        return list;
    }

    public sealed record ScsiAddress(byte PathId, byte TargetId, byte Lun);

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, ScsiAddress>
        PreferredAddress = new();

    /// <summary>
    /// USB mostík môže mať disk na inom LUN, než aký hlási SCSI_GET_ADDRESS.
    /// Preto vyskúšame okolité adresy a podľa INQUIRY vyberieme tú, ktorá
    /// zodpovedá modelu disku (napr. WDC namiesto ASM1352-PM).
    /// </summary>
    private static List<ScsiAddress> DiscoverAddresses(int diskNumber,
        SafeFileHandle handle, string expectedModel, out string bridgeProduct)
    {
        bridgeProduct = "";
        var list = new List<ScsiAddress>();
        void Add(byte path, byte target, byte lun)
        {
            var address = new ScsiAddress(path, target, lun);
            if (!list.Contains(address)) list.Add(address);
        }

        byte pathId = 0, targetId = 0, lun = 0;
        if (TryGetAddress(handle, out pathId, out targetId, out lun))
            Add(pathId, targetId, lun);

        var inquiry = SendDirect(handle, pathId, targetId, lun,
            new byte[] { 0x12, 0, 0, 0, 96, 0 }, 6, 96, 2,
            out _, out _, out _, out _);
        bridgeProduct = InquiryProduct(inquiry);
        LastBridge = bridgeProduct;

        for (byte l = 0; l < 4; l++) Add(pathId, targetId, l);
        for (byte t = 0; t < 2; t++) Add(pathId, t, 0);
        for (byte p = 0; p < 2; p++) Add(p, 0, 0);

        var expected = NormalizeModel(expectedModel);
        if (expected.Length > 0)
        {
            var scan = System.Diagnostics.Stopwatch.StartNew();
            int Score(ScsiAddress address)
            {
                if (scan.ElapsedMilliseconds > 3000) return 0;
                return InquiryScore(handle, address, expected);
            }

            var scored = list
                .Select(a => (Address: a, Score: Score(a)))
                .OrderByDescending(x => x.Score)
                .ToList();
            if (scored.Count > 0 && scored[0].Score > 0)
                list = scored.Select(x => x.Address).ToList();
        }

        if (PreferredAddress.TryGetValue(diskNumber, out var preferred))
        {
            int index = list.IndexOf(preferred);
            if (index > 0) { list.RemoveAt(index); list.Insert(0, preferred); }
            else if (index < 0) list.Insert(0, preferred);
        }
        return list;
    }

    private static int InquiryScore(SafeFileHandle handle, ScsiAddress address, string expected)
    {
        var data = SendDirect(handle, address.PathId, address.TargetId, address.Lun,
            new byte[] { 0x12, 0, 0, 0, 96, 0 }, 6, 96, 2,
            out _, out _, out _, out _);
        if (data is null || data.Length < 36) return 0;

        var text = NormalizeModel(InquiryProduct(data));
        if (text.Length == 0) return 0;
        if (text == expected) return 100;
        if (expected.Contains(text, StringComparison.Ordinal) ||
            text.Contains(expected, StringComparison.Ordinal)) return 80;
        if (text.Length >= 8 && expected.Length >= 8 &&
            text[..8] == expected[..8]) return 60;
        return 0;
    }

    private static string InquiryProduct(byte[]? data)
    {
        if (data is null || data.Length < 32) return "";
        var vendor = Encoding.ASCII.GetString(data, 8, 8).Trim();
        var product = Encoding.ASCII.GetString(data, 16, 16).Trim();
        return (vendor + " " + product).Trim();
    }

    private static string NormalizeModel(string value)
        => new(value.Where(char.IsLetterOrDigit).ToArray());

    private static byte[]? ReadAtaScsi(int diskNumber, byte command, byte features,
        byte count, byte lbaLow, byte lbaMid, byte lbaHigh, bool requireSmart,
        int budgetMs = 15000, string expectedModel = "")
    {
        lock (Gate(diskNumber))
        {
            if (!EnterScsi())
            {
                LastError = "SCSI je zaneprázdnené alebo vypnuté";
                return null;
            }
            try
            {
                return ReadAtaScsiCore(diskNumber, command, features, count,
                    lbaLow, lbaMid, lbaHigh, requireSmart, budgetMs, expectedModel);
            }
            finally
            {
                LeaveScsi();
            }
        }
    }

    private static byte[]? ReadAtaScsiCore(int diskNumber, byte command, byte features,
        byte count, byte lbaLow, byte lbaMid, byte lbaHigh, bool requireSmart, int budgetMs,
        string expectedModel)
    {
        SafeFileHandle? handle = null;
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

            var addresses = DiscoverAddresses(diskNumber, handle, expectedModel,
                out string bridgeProduct);
            if (addresses.Count == 0) addresses.Add(new ScsiAddress(0, 0, 0));
            bool preferAsm = bridgeProduct.Contains("ASM1352", StringComparison.OrdinalIgnoreCase);

            var budget = System.Diagnostics.Stopwatch.StartNew();
            const int timeoutSeconds = 2;

            // Kým sa 3,5" disk roztočí, mostík chvíľu hlási "not ready".
            // Skúsime SAT12 na hlásenej adrese niekoľkokrát s krátkou pauzou.
            var first = addresses[0];
            var sat12 = BuildVariants(diskNumber, command, features, count,
                lbaLow, lbaMid, lbaHigh, first.Lun, preferAsm: false)[0];
            for (int attempt = 0; attempt < 12 && budget.ElapsedMilliseconds < budgetMs; attempt++)
            {
                byte status = 0;
                string sense = "";
                int win32 = 0;
                int returned = 0;
                var page = SendDirect(handle, first.PathId, first.TargetId, first.Lun,
                    sat12.Cdb, sat12.CdbLength, PageSize, timeoutSeconds,
                    out status, out sense, out win32, out returned);
                if (page is not null && IsUsable(page, requireSmart))
                {
                    Preferred[diskNumber] = sat12.Name;
                    PreferredAddress[diskNumber] = first;
                    LastAddress = $"{first.PathId}/{first.TargetId}/{first.Lun}";
                    LastProtocol = $"{sat12.Name} {sat12.Cdb[0]:X2}/{sat12.Cdb[1]:X2}";
                    return page;
                }
                if (!SenseNotReady(sense)) break;
                if (attempt == 0) StartUnit(diskNumber);
                Thread.Sleep(400);
            }

            foreach (var address in addresses)
            {
                if (budget.ElapsedMilliseconds > budgetMs) { errors.Add("timeout"); break; }
                LastAddress = $"{address.PathId}/{address.TargetId}/{address.Lun}";

                var variants = BuildVariants(diskNumber, command, features, count,
                    lbaLow, lbaMid, lbaHigh, address.Lun, preferAsm);

                foreach (bool direct in new[] { true, false })
                {
                    foreach (var variant in variants)
                    {
                        if (budget.ElapsedMilliseconds > budgetMs)
                        {
                            errors.Add("timeout");
                            break;
                        }

                        byte status = 0;
                        string sense = "";
                        int win32 = 0;
                        int returned = 0;
                        var page = direct
                            ? SendDirect(handle, address.PathId, address.TargetId, address.Lun,
                                variant.Cdb, variant.CdbLength, PageSize, timeoutSeconds,
                                out status, out sense, out win32, out returned)
                            : SendNonDirect(handle, address.PathId, address.TargetId, address.Lun,
                                variant.Cdb, variant.CdbLength, PageSize, timeoutSeconds,
                                out status, out sense, out win32, out returned);

                        if (page is null)
                        {
                            errors.Add($"{address.Lun}:{(direct ? "D" : "N")}:{variant.Name} " +
                                       $"ioctl={win32} status={status}");
                            continue;
                        }
                        if (!IsUsable(page, requireSmart))
                        {
                            errors.Add($"{address.Lun}:{(direct ? "D" : "N")}:{variant.Name} prázdne");
                            continue;
                        }

                        Preferred[diskNumber] = variant.Name;
                        PreferredAddress[diskNumber] = address;
                        LastProtocol = (direct ? "" : "ND ") +
                                       $"{variant.Name} {variant.Cdb[0]:X2}/{variant.Cdb[1]:X2}";
                        return page;
                    }
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
            try { handle?.Dispose(); } catch { }
        }
    }

    public sealed record StorageDescriptor(string Vendor, string Product, string Revision,
        string Serial, int BusType);

    /// <summary>Čo o disku hlási Windows cez IOCTL_STORAGE_QUERY_PROPERTY (bezpečné, bez SCSI).</summary>
    public static StorageDescriptor? QueryStorageDescriptor(int diskNumber)
    {
        SafeFileHandle? handle = null;
        try
        {
            handle = CreateFileW($@"\\.\PhysicalDrive{diskNumber}",
                GenericRead, FileShareRead | FileShareWrite,
                IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid) return null;

            var query = new byte[12];
            var output = new byte[1024];
            if (!DeviceIoControl(handle, 0x002D1400, query, query.Length,
                    output, output.Length, out int returned, IntPtr.Zero) || returned < 36)
                return null;

            string At(int offset)
            {
                if (offset <= 0 || offset >= output.Length) return "";
                int end = Array.IndexOf(output, (byte)0, offset);
                if (end < 0) end = output.Length;
                return Encoding.ASCII.GetString(output, offset, end - offset).Trim();
            }

            return new StorageDescriptor(At(BitConverter.ToInt32(output, 12)),
                At(BitConverter.ToInt32(output, 16)),
                At(BitConverter.ToInt32(output, 20)),
                At(BitConverter.ToInt32(output, 24)),
                BitConverter.ToInt32(output, 28));
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

    public static (string Serial, string Model)? ReadInquiry(int diskNumber)
    {
        var standard = ReadScsiInquiry(diskNumber, evpd: false, page: 0x00, 255);
        var vpd = ReadScsiInquiry(diskNumber, evpd: true, page: 0x80, 255);

        string serial = "";
        string model = "";
        if (vpd is { Length: > 4 })
        {
            int length = (vpd[2] << 8) | vpd[3];
            int count = Math.Min(length, vpd.Length - 4);
            serial = Encoding.ASCII.GetString(vpd, 4, count).Trim('\0', ' ');
        }

        if (standard is { Length: >= 36 })
        {
            var vendor = Encoding.ASCII.GetString(standard, 8, 8).Trim();
            var product = Encoding.ASCII.GetString(standard, 16, 16).Trim();
            model = (vendor + " " + product).Trim();
        }

        if (serial.Length == 0 && model.Length == 0) return null;
        return (serial, model);
    }

    private static byte[]? ReadScsiInquiry(int diskNumber, bool evpd, byte page, int dataLength)
    {
        if (!EnterScsi()) return null;
        try
        {
            return ReadScsiInquiryCore(diskNumber, evpd, page, dataLength);
        }
        finally
        {
            LeaveScsi();
        }
    }

    private static byte[]? ReadScsiInquiryCore(int diskNumber, bool evpd, byte page, int dataLength)
    {
        SafeFileHandle? handle = null;
        IntPtr buffer = IntPtr.Zero;
        IntPtr data = IntPtr.Zero;
        try
        {
            handle = CreateFileW($@"\\.\PhysicalDrive{diskNumber}",
                GenericRead | GenericWrite, FileShareRead | FileShareWrite,
                IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
            if (handle.IsInvalid) return null;

            byte pathId = 0, targetId = 0, lun = 0;
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

            int structSize = Marshal.SizeOf<ScsiPassThroughDirect>();
            const int senseSize = 32;
            data = Marshal.AllocHGlobal(dataLength);
            buffer = Marshal.AllocHGlobal(structSize + senseSize);

            var cdb = new byte[16];
            cdb[0] = 0x12; // INQUIRY
            cdb[1] = evpd ? (byte)0x01 : (byte)0x00;
            cdb[2] = page;
            cdb[3] = 0;
            cdb[4] = (byte)dataLength;

            var packet = new ScsiPassThroughDirect
            {
                Length = (ushort)structSize,
                PathId = pathId,
                TargetId = targetId,
                Lun = lun,
                CdbLength = 6,
                SenseInfoLength = senseSize,
                DataIn = 1,
                DataTransferLength = (uint)dataLength,
                TimeOutValue = 5,
                DataBuffer = data,
                SenseInfoOffset = (uint)structSize,
                Cdb = PadCdb(cdb)
            };

            for (int i = 0; i < structSize + senseSize; i++) Marshal.WriteByte(buffer, i, 0);
            Marshal.StructureToPtr(packet, buffer, false);

            if (!DeviceIoControlPtr(handle, IoctlScsiPassThroughDirect,
                    buffer, structSize + senseSize, buffer, structSize + senseSize,
                    out int returned, IntPtr.Zero) || returned <= 0)
                return null;

            var back = Marshal.PtrToStructure<ScsiPassThroughDirect>(buffer);
            var sense = new byte[senseSize];
            Marshal.Copy(buffer + structSize, sense, 0, senseSize);
            if (back.ScsiStatus != 0 && !AtaRecoveredOk(sense)) return null;

            var result = new byte[dataLength];
            Marshal.Copy(data, result, 0, dataLength);
            return result;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (data != IntPtr.Zero) Marshal.FreeHGlobal(data);
            try { handle?.Dispose(); } catch { }
        }
    }

    public sealed class AtaProbeLine
    {
        public string Name = "";
        public string Transport = "";
        public string Address = "";
        public string Cdb = "";
        public bool IoctlOk;
        public int Win32Error;
        public int Returned;
        public byte ScsiStatus;
        public string Sense = "";
        public string Asc = "";
        public long Ms;
        public string Outcome = "";
    }

    public sealed class AtaProbe
    {
        public string Address = "";
        public string Error = "";
        public string Success = "";
        public string WakeUp = "";
        public int DirectStructSize;
        public int NonDirectStructSize;
        public byte[]? Page;
        public List<AtaProbeLine> Lines = new();
    }

    /// <summary>Vyskúša všetky varianty ATA PASS-THROUGH a vráti presný výsledok.</summary>
    public static AtaProbe Probe(int diskNumber, bool smart, int timeoutSeconds = 2,
        string expectedModel = "")
    {
        var result = new AtaProbe
        {
            DirectStructSize = Marshal.SizeOf<ScsiPassThroughDirect>(),
            NonDirectStructSize = Marshal.SizeOf<ScsiPassThrough>()
        };

        lock (Gate(diskNumber))
        {
            if (!EnterScsi())
            {
                result.Error = ScsiDisabled
                    ? "SCSI je vypnuté (ZDENKO_NO_SCSI)"
                    : "SCSI je zaneprázdnené";
                return result;
            }

            try
            {
            WakeUpDisk(diskNumber);
            result.WakeUp = LastWakeUp;
            SafeFileHandle? handle = null;
            try
            {
                handle = CreateFileW($@"\\.\PhysicalDrive{diskNumber}",
                    GenericRead | GenericWrite, FileShareRead | FileShareWrite,
                    IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    result.Error = "open " + Marshal.GetLastWin32Error();
                    return result;
                }

                var addresses = DiscoverAddresses(diskNumber, handle, expectedModel,
                    out string bridgeProduct);
                if (addresses.Count == 0) addresses.Add(new ScsiAddress(0, 0, 0));
                bool preferAsm = bridgeProduct.Contains("ASM1352", StringComparison.OrdinalIgnoreCase);
                // Ak sa podarilo nájsť adresu podľa modelu, testujeme len ju.
                if (NormalizeModel(expectedModel).Length > 0)
                    addresses = addresses.Take(1).ToList();
                result.Address = string.Join(", ",
                    addresses.Select(a => $"{a.PathId}/{a.TargetId}/{a.Lun}"));

                byte command = smart ? (byte)0xB0 : (byte)0xEC;
                byte features = smart ? (byte)0xD0 : (byte)0x00;
                byte count = smart ? (byte)0x01 : (byte)0x01;
                byte lbaLow = smart ? (byte)0x01 : (byte)0x00;
                byte lbaMid = smart ? (byte)0x4F : (byte)0x00;
                byte lbaHigh = smart ? (byte)0xC2 : (byte)0x00;

                var total = System.Diagnostics.Stopwatch.StartNew();
                foreach (var address in addresses)
                {
                    var variants = BuildVariants(diskNumber, command, features, count,
                        lbaLow, lbaMid, lbaHigh, address.Lun, preferAsm,
                        includeAsm: AsmEnabled);
                    string addressText = $"{address.PathId}/{address.TargetId}/{address.Lun}";

                    foreach (bool direct in new[] { true, false })
                    {
                        foreach (var variant in variants)
                        {
                            if (total.ElapsedMilliseconds > 20000)
                            {
                                result.Error = "časový limit 20 s";
                                return result;
                            }
                            var line = new AtaProbeLine
                            {
                                Name = variant.Name,
                                Transport = direct ? "direct" : "non-direct",
                                Address = addressText,
                                Cdb = Convert.ToHexString(variant.Cdb, 0, variant.CdbLength)
                            };
                            var watch = System.Diagnostics.Stopwatch.StartNew();
                            string senseHex = "";
                            var page = direct
                                ? SendDirect(handle, address.PathId, address.TargetId, address.Lun,
                                    variant.Cdb, variant.CdbLength, PageSize, timeoutSeconds,
                                    out line.ScsiStatus, out senseHex,
                                    out line.Win32Error, out line.Returned)
                                : SendNonDirect(handle, address.PathId, address.TargetId, address.Lun,
                                    variant.Cdb, variant.CdbLength, PageSize, timeoutSeconds,
                                    out line.ScsiStatus, out senseHex,
                                    out line.Win32Error, out line.Returned);
                            watch.Stop();
                            line.Ms = watch.ElapsedMilliseconds;
                            line.Sense = senseHex;
                            line.IoctlOk = line.Win32Error == 0 && line.Returned > 0;
                            line.Asc = DecodeSense(senseHex);

                            if (line.IoctlOk && line.ScsiStatus == 0)
                            {
                                if (page is not null && IsUsable(page, smart))
                                {
                                    line.Outcome = "OK";
                                    result.Success = $"{line.Transport} {variant.Name} na {addressText}";
                                    result.Page = page;
                                    Preferred[diskNumber] = variant.Name;
                                    PreferredAddress[diskNumber] = address;
                                    result.Lines.Add(line);
                                    return result;
                                }
                                line.Outcome = "prázdne dáta";
                            }
                            else if (line.IoctlOk)
                            {
                                line.Outcome = "SCSI status " + line.ScsiStatus;
                            }
                            else
                            {
                                line.Outcome = "IOCTL " + line.Win32Error;
                            }
                            result.Lines.Add(line);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }
            finally
            {
                try { handle?.Dispose(); } catch { }
            }
            }
            finally
            {
                LeaveScsi();
            }
        }

        return result;
    }

    private static bool IsUsable(byte[] page, bool smart)
    {
        if (smart)
        {
            var attributes = ParseAttributes(page);
            return attributes.Count >= 3 &&
                   attributes.Any(a => a.Id is 5 or 9 or 194 or 197 or 198);
        }
        var (serial, model) = ParseIdentify(page);
        return serial.Length > 0 || model.Length > 0;
    }

    private static bool SenseNotReady(string senseHex)
    {
        if (string.IsNullOrWhiteSpace(senseHex)) return false;
        try
        {
            var bytes = Convert.FromHexString(senseHex);
            if (bytes.Length < 14) return false;
            int key = (bytes[0] & 0x7F) is 0x72 or 0x73
                ? bytes[1] & 0x0F
                : bytes[2] & 0x0F;
            int asc = bytes[12];
            return key == 0x02 || asc == 0x3A;
        }
        catch
        {
            return false;
        }
    }

    private static string DecodeSense(string senseHex)
    {
        if (string.IsNullOrWhiteSpace(senseHex)) return "";
        try
        {
            var bytes = Convert.FromHexString(senseHex);
            if (bytes.Length < 14) return "";
            int key = (bytes[0] & 0x7F) is 0x72 or 0x73
                ? bytes[1] & 0x0F
                : bytes[2] & 0x0F;
            int asc = bytes[12];
            int ascq = bytes[13];
            string text = $"key={key} asc={asc:X2} ascq={ascq:X2}";
            return key switch
            {
                0x05 when asc == 0x20 => text + " (neplatný príkaz)",
                0x05 when asc == 0x24 => text + " (neplatné pole v CDB)",
                0x05 when asc == 0x25 => text + " (neplatný LUN)",
                0x02 => text + " (disk nie je pripravený)",
                0x04 => text + " (hardvérová chyba)",
                _ => text
            };
        }
        catch
        {
            return "";
        }
    }

    private static bool TryGetAddress(SafeFileHandle handle,
        out byte pathId, out byte targetId, out byte lun)
    {
        pathId = 0;
        targetId = 0;
        lun = 0;
        IntPtr address = Marshal.AllocHGlobal(8);
        try
        {
            for (int i = 0; i < 8; i++) Marshal.WriteByte(address, i, 0);
            if (!DeviceIoControlPtr(handle, IoctlScsiGetAddress,
                    IntPtr.Zero, 0, address, 8, out int addressReturned, IntPtr.Zero) ||
                addressReturned < 8)
                return false;
            pathId = Marshal.ReadByte(address, 5);
            targetId = Marshal.ReadByte(address, 6);
            lun = Marshal.ReadByte(address, 7);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(address);
        }
    }

    private static byte[] PadCdb(byte[] cdb)
    {
        if (cdb.Length == 16) return cdb;
        var padded = new byte[16];
        Array.Copy(cdb, padded, Math.Min(16, cdb.Length));
        return padded;
    }

    private static byte[]? SendDirect(SafeFileHandle handle, byte pathId, byte targetId,
        byte lun, byte[] cdb, byte cdbLength, int dataLength, int timeoutSeconds,
        out byte scsiStatus, out string senseHex, out int win32, out int returned)
    {
        scsiStatus = 0;
        senseHex = "";
        win32 = 0;
        returned = 0;

        int structSize = Marshal.SizeOf<ScsiPassThroughDirect>();
        const int senseSize = 32;
        IntPtr data = Marshal.AllocHGlobal(Math.Max(1, dataLength));
        IntPtr buffer = Marshal.AllocHGlobal(structSize + senseSize);
        try
        {
            var packet = new ScsiPassThroughDirect
            {
                Length = (ushort)structSize,
                PathId = pathId,
                TargetId = targetId,
                Lun = lun,
                CdbLength = cdbLength,
                SenseInfoLength = senseSize,
                DataIn = dataLength > 0 ? (byte)1 : (byte)0,
                DataTransferLength = (uint)dataLength,
                TimeOutValue = (uint)timeoutSeconds,
                DataBuffer = data,
                SenseInfoOffset = (uint)structSize,
                Cdb = PadCdb(cdb)
            };

            for (int i = 0; i < structSize + senseSize; i++) Marshal.WriteByte(buffer, i, 0);
            Marshal.StructureToPtr(packet, buffer, false);

            if (!DeviceIoControlPtr(handle, IoctlScsiPassThroughDirect,
                    buffer, structSize + senseSize, buffer, structSize + senseSize,
                    out returned, IntPtr.Zero) || returned <= 0)
            {
                win32 = Marshal.GetLastWin32Error();
                return null;
            }

            var back = Marshal.PtrToStructure<ScsiPassThroughDirect>(buffer);
            scsiStatus = back.ScsiStatus;
            var sense = new byte[senseSize];
            Marshal.Copy(buffer + structSize, sense, 0, senseSize);
            senseHex = Convert.ToHexString(sense);
            if (scsiStatus != 0) return null;

            var page = new byte[dataLength];
            Marshal.Copy(data, page, 0, dataLength);
            return page;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            Marshal.FreeHGlobal(data);
        }
    }

    private static byte[]? SendNonDirect(SafeFileHandle handle, byte pathId, byte targetId,
        byte lun, byte[] cdb, byte cdbLength, int dataLength, int timeoutSeconds,
        out byte scsiStatus, out string senseHex, out int win32, out int returned)
    {
        scsiStatus = 0;
        senseHex = "";
        win32 = 0;
        returned = 0;

        int structSize = Marshal.SizeOf<ScsiPassThrough>();
        const int senseSize = 32;
        int size = structSize + dataLength + senseSize;
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try
        {
            for (int i = 0; i < size; i++) Marshal.WriteByte(buffer, i, 0);
            var packet = new ScsiPassThrough
            {
                Length = (ushort)structSize,
                PathId = pathId,
                TargetId = targetId,
                Lun = lun,
                CdbLength = cdbLength,
                SenseInfoLength = senseSize,
                DataIn = dataLength > 0 ? (byte)1 : (byte)0,
                DataTransferLength = (uint)dataLength,
                TimeOutValue = (uint)timeoutSeconds,
                DataBufferOffset = new UIntPtr((uint)structSize),
                SenseInfoOffset = (uint)(structSize + dataLength),
                Cdb = PadCdb(cdb)
            };
            Marshal.StructureToPtr(packet, buffer, false);

            if (!DeviceIoControlPtr(handle, IoctlScsiPassThrough,
                    buffer, size, buffer, size, out returned, IntPtr.Zero) || returned <= 0)
            {
                win32 = Marshal.GetLastWin32Error();
                return null;
            }

            var back = Marshal.PtrToStructure<ScsiPassThrough>(buffer);
            scsiStatus = back.ScsiStatus;
            var sense = new byte[senseSize];
            Marshal.Copy(buffer + structSize + dataLength, sense, 0, senseSize);
            senseHex = Convert.ToHexString(sense);
            if (scsiStatus != 0) return null;

            var page = new byte[dataLength];
            Marshal.Copy(buffer + structSize, page, 0, dataLength);
            return page;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>SCSI INQUIRY, VPD 0x80 a SCSI adresa pre diagnostiku.</summary>
    public static List<string> ScsiInfo(int diskNumber, string expectedModel = "")
    {
        var lines = new List<string>();
        lock (Gate(diskNumber))
        {
            if (!EnterScsi())
            {
                lines.Add(ScsiDisabled
                    ? "SCSI je vypnuté (ZDENKO_NO_SCSI)"
                    : "SCSI je zaneprázdnené");
                return lines;
            }

            try
            {
            lines.Add("Prebudenie disku: " + WakeUpDisk(diskNumber));
            SafeFileHandle? handle = null;
            try
            {
                handle = CreateFileW($@"\\.\PhysicalDrive{diskNumber}",
                    GenericRead | GenericWrite, FileShareRead | FileShareWrite,
                    IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    lines.Add("open zlyhalo: " + Marshal.GetLastWin32Error());
                    return lines;
                }

                var addresses = DiscoverAddresses(diskNumber, handle, expectedModel,
                        out string bridgeProduct)
                    .Take(4).ToList();
                if (addresses.Count == 0) addresses.Add(new ScsiAddress(0, 0, 0));
                lines.Add($"Mostík (INQUIRY): '{bridgeProduct}'");
                lines.Add("SCSI adresy: " + string.Join(", ",
                    addresses.Select(a => $"{a.PathId}/{a.TargetId}/{a.Lun}")));

                // Zámerne posielame len INQUIRY a VPD 0x80. Tieto príkazy sú
                // overené na viacerých mostíkoch; REPORT LUNS, VPD 0x83 ani
                // READ CAPACITY neposielame — na niektorých firmvéroch USB
                // mostíkov dokážu zraziť systémový ovládač.
                foreach (var address in addresses)
                {
                    lines.Add($"-- adresa {address.PathId}/{address.TargetId}/{address.Lun} --");
                    SendCdb(handle, address.PathId, address.TargetId, address.Lun,
                        new byte[] { 0x12, 0, 0, 0, 96, 0 }, 96, lines, "INQUIRY");
                    SendCdb(handle, address.PathId, address.TargetId, address.Lun,
                        new byte[] { 0x12, 1, 0x80, 0, 255, 0 }, 255, lines, "VPD 0x80");
                }
            }
            catch (Exception ex)
            {
                lines.Add("chyba: " + ex.Message);
            }
            finally
            {
                try { handle?.Dispose(); } catch { }
            }
            }
            finally
            {
                LeaveScsi();
            }
        }
        return lines;
    }

    private static void SendCdb(SafeFileHandle handle, byte pathId, byte targetId, byte lun,
        byte[] cdb, int length, List<string> lines, string label)
    {
        var data = SendDirect(handle, pathId, targetId, lun, cdb, (byte)cdb.Length,
            length, 3, out byte status, out string sense, out int win32, out int returned);
        if (data is null)
        {
            lines.Add($"{label}: zlyhalo (ioctl={win32}, status={status}, sense={sense}, " +
                      $"vrátené={returned})");
            return;
        }

        int used = Math.Min(length, 32);
        lines.Add($"{label}: {Convert.ToHexString(data, 0, used)}");
        if (label == "INQUIRY" && data.Length >= 36)
        {
            var vendor = Encoding.ASCII.GetString(data, 8, 8).Trim();
            var product = Encoding.ASCII.GetString(data, 16, 16).Trim();
            var revision = Encoding.ASCII.GetString(data, 32, 4).Trim();
            lines.Add($"  typ={data[0] & 0x1F} vendor='{vendor}' produkt='{product}' " +
                      $"revízia='{revision}'");
        }
        if (label is "VPD 0x80" && data.Length > 4)
        {
            int count = Math.Min(((data[2] << 8) | data[3]), data.Length - 4);
            lines.Add("  sériové číslo: " +
                      Encoding.ASCII.GetString(data, 4, Math.Max(0, count)).Trim('\0', ' '));
        }
        if (label == "REPORT LUNS" && data.Length >= 8)
        {
            int count = Math.Min(data[3], (data.Length - 8) / 8);
            var luns = new List<string>();
            for (int i = 0; i < count; i++)
            {
                int offset = 8 + i * 8;
                if (offset + 1 >= data.Length) break;
                luns.Add(data[offset].ToString("X2"));
            }
            lines.Add("  LUNy: " + (luns.Count == 0 ? "(žiadne)" : string.Join(", ", luns)));
        }
        if (label == "READ CAPACITY" && data.Length >= 8)
        {
            uint lastLba = (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);
            uint block = (uint)((data[4] << 24) | (data[5] << 16) | (data[6] << 8) | data[7]);
            if (block > 0) lines.Add($"  kapacita: {(lastLba + 1L) * block} B, blok {block} B");
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

    public static (string Serial, string Model) ParseIdentifyPublic(byte[] page)
        => ParseIdentify(page);

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



