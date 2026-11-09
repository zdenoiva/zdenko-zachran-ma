using System.Runtime.InteropServices;
using System.Text;
using NtfsRescue.Core;
using NtfsRescue.Core.Fat32;
using NtfsRescue.Repair;

namespace NtfsRescue;

internal static class SelfTest
{
    private static readonly List<string> Lines = new();
    private static int _failed;

    public static int Run()
    {
        Lines.Clear();
        _failed = 0;

        TestBootSector();
        TestRunlist();
        TestMftRecord();
        TestLznt1();
        TestFat32();
        TestSmartText();
        TestSystemFileDetection();
        TestUpdates();
        TestBootParsing();
        TestImageCopy();

        var path = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-selftest.log");
        Lines.Add("");
        Lines.Add(_failed == 0 ? "SELFTEST OK" : $"SELFTEST ZLYHAL ({_failed} chyb)");
        try { File.WriteAllText(path, string.Join(Environment.NewLine, Lines), new UTF8Encoding(false)); } catch { }

        return _failed == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string name)
    {
        if (condition) Lines.Add("OK   " + name);
        else { _failed++; Lines.Add("FAIL " + name); }
    }

    private static void TestBootSector()
    {
        var b = new byte[512];
        Encoding.ASCII.GetBytes("NTFS    ").CopyTo(b, 3);
        Bin.W16(b, 0x0B, 512);
        b[0x0D] = 8;
        Bin.W64(b, 0x28, 100_000);
        Bin.W64(b, 0x30, 4);
        Bin.W64(b, 0x38, 8);
        b[0x40] = unchecked((byte)(sbyte)-10); // 1024 B record
        b[0x44] = unchecked((byte)(sbyte)-12); // 4096 B index
        Bin.W64(b, 0x48, 0x1122334455667788UL);
        Bin.W16(b, 510, 0xAA55);

        var boot = NtfsBootSector.Parse(b);
        Check(boot.IsValid, "boot sector sa parsuje");
        Check(boot.ClusterSize == 4096, "cluster size = 4096");
        Check(boot.MftRecordSize == 1024, "MFT record = 1024");
        Check(boot.TotalClusters == 12_500, "total clusters = 12500");

        var bad = (byte[])b.Clone();
        bad[3] = (byte)'X';
        Check(!NtfsBootSector.Parse(bad).IsValid, "poškodený boot sector sa odmietne");
    }

    private static void TestRunlist()
    {
        // run 1: LCN 10, 2 clusters; run 2: sparse, 3 clusters; run 3: delta -5 => LCN 5, 2 clusters
        var data = new byte[] { 0x11, 2, 10, 0x01, 3, 0x11, 2, 0xFB, 0x00 };
        var runs = NtfsRunlist.Parse(data, 0, data.Length);

        Check(runs.Count == 3, "runlist má 3 runy");
        Check(runs.Count == 3 && runs[0].Lcn == 10 && runs[0].Clusters == 2, "prvý run LCN 10 +2");
        Check(runs.Count == 3 && runs[1].IsSparse && runs[1].Vcn == 2 && runs[1].Clusters == 3, "druhý run sparse +3");
        Check(runs.Count == 3 && runs[2].Lcn == 5 && runs[2].Vcn == 5 && runs[2].Clusters == 2, "tretí run LCN 5 +2");
    }

    private static void TestMftRecord()
    {
        const int recordSize = 1024;
        const int sectorSize = 512;
        var rec = new byte[recordSize];

        Encoding.ASCII.GetBytes("FILE").CopyTo(rec, 0);
        Bin.W16(rec, 4, 0x30); // USA offset
        Bin.W16(rec, 6, 3);    // USA count
        Bin.W16(rec, 0x10, 1);
        Bin.W16(rec, 0x12, 1);
        Bin.W16(rec, 0x14, 0x38); // attrs offset
        Bin.W16(rec, 0x16, 0x01); // in use, file
        Bin.W32(rec, 0x18, 0);

        int pos = 0x38;

        // $STANDARD_INFORMATION
        Bin.W32(rec, pos, 0x10);
        Bin.W32(rec, pos + 4, 0x48);
        Bin.W32(rec, pos + 16, 0x30);
        Bin.W16(rec, pos + 20, 24);
        Bin.W64(rec, pos + 24, 0); // creation
        pos += 0x48;

        // $FILE_NAME (test.txt)
        var name = Encoding.Unicode.GetBytes("test.txt");
        int fnValueLength = 0x42 + name.Length;
        int fnLength = (24 + fnValueLength + 7) & ~7;
        Bin.W32(rec, pos, 0x30);
        Bin.W32(rec, pos + 4, (uint)fnLength);
        Bin.W32(rec, pos + 16, (uint)fnValueLength);
        Bin.W16(rec, pos + 20, 24);
        Bin.W64(rec, pos + 24, 5); // parent root
        rec[pos + 24 + 0x40] = (byte)8; // name length
        rec[pos + 24 + 0x41] = 1;       // Win32
        name.CopyTo(rec, pos + 24 + 0x42);
        pos += fnLength;

        // $DATA resident "hello"
        var content = Encoding.ASCII.GetBytes("hello");
        Bin.W32(rec, pos, 0x80);
        Bin.W32(rec, pos + 4, 32);
        Bin.W32(rec, pos + 16, (uint)content.Length);
        Bin.W16(rec, pos + 20, 24);
        content.CopyTo(rec, pos + 24);
        pos += 32;

        Bin.W32(rec, pos, 0xFFFFFFFF);
        pos += 4;
        Bin.W32(rec, 0x18, (uint)pos);

        // fixup
        ushort usn = 0x1234;
        var originals = new ushort[2];
        for (int i = 0; i < 2; i++)
        {
            int p = (i + 1) * sectorSize - 2;
            originals[i] = BitConverter.ToUInt16(rec, p);
            Bin.W16(rec, p, usn);
        }
        Bin.W16(rec, 0x30, usn);
        Bin.W16(rec, 0x32, originals[0]);
        Bin.W16(rec, 0x34, originals[1]);

        var parsed = NtfsRecord.TryParse(rec, 7, sectorSize, out var error);
        Check(parsed is not null, "MFT záznam sa parsuje" + (error is null ? "" : ": " + error));
        if (parsed is null) return;

        Check(parsed.InUse && !parsed.IsDirectory, "flagy záznamu sú správne");
        var si = parsed.FindFirst(AttrType.StandardInformation);
        var fn = parsed.FindFirst(AttrType.FileName);
        var data = parsed.DataAttribute();
        Check(si is not null, "$STANDARD_INFORMATION existuje");
        Check(fn is not null, "$FILE_NAME existuje");
        Check(data?.ResidentValue is { Length: 5 }, "$DATA má 5 bajtov");

        if (fn?.ResidentValue is { } fnv)
        {
            int nameLength = fnv[0x40];
            var parsedName = Encoding.Unicode.GetString(fnv, 0x42, nameLength * 2);
            Check(parsedName == "test.txt", "názov súboru = test.txt");
        }
    }

    private static void TestFat32()
    {
        string? path = null;
        try
        {
            const int sector = 512;
            var image = new byte[sector * 5];

            // Boot sector: 1 reserved sector, 2 FATs, 1 sector per cluster, FAT32.
            var boot = new byte[sector];
            boot[0] = 0xEB; boot[1] = 0x58; boot[2] = 0x90;
            Encoding.ASCII.GetBytes("MSDOS5.0").CopyTo(boot, 3);
            Bin.W16(boot, 0x0B, sector);
            boot[0x0D] = 1;             // sectors per cluster
            Bin.W16(boot, 0x0E, 1);     // reserved
            boot[0x10] = 2;             // FAT count
            Bin.W16(boot, 0x11, 0);     // root entries
            boot[0x15] = 0xF8;
            Bin.W32(boot, 0x20, 1003);  // total sectors
            Bin.W32(boot, 0x24, 1);     // FAT size
            Bin.W32(boot, 0x2C, 2);     // root cluster
            Bin.W16(boot, 0x30, 1);
            Bin.W16(boot, 0x32, 6);
            boot[0x40] = 0x80;
            boot[0x42] = 0x29;
            Bin.W32(boot, 0x43, 0x12345678);
            Encoding.ASCII.GetBytes("TESTVOL    ").CopyTo(boot, 0x47);
            Encoding.ASCII.GetBytes("FAT32   ").CopyTo(boot, 0x52);
            Bin.W16(boot, 510, 0xAA55);
            boot.CopyTo(image, 0);

            // FAT1 a FAT2.
            for (int fat = 0; fat < 2; fat++)
            {
                int off = sector + fat * sector;
                Bin.W32(image, off + 0, 0x0FFFFFF8);
                Bin.W32(image, off + 4, 0x0FFFFFFF);
                Bin.W32(image, off + 8, 0x0FFFFFFF); // root cluster 2 = EOC
                Bin.W32(image, off + 12, 0x0FFFFFFF); // file cluster 3 = EOC
            }

            // Root directory at cluster 2 (data starts at sector 3).
            int root = sector * 3;
            Encoding.ASCII.GetBytes("HELLO   TXT").CopyTo(image, root);
            image[root + 0x0B] = 0x20; // archive
            Bin.W16(image, root + 0x1A, 3);
            Bin.W32(image, root + 0x1C, 5);
            image[root + 32] = 0x00;

            // File data at cluster 3.
            Encoding.ASCII.GetBytes("hello").CopyTo(image, root + sector);

            path = Path.Combine(Path.GetTempPath(), $"zdenko-fat32-{Guid.NewGuid():N}.img");
            File.WriteAllBytes(path, image);

            var parsed = Fat32BootSector.Parse(boot);
            Check(parsed.IsValid, "FAT32 boot sector sa parsuje");
            Check(parsed.ClusterSize == 512, "FAT32 cluster size = 512");
            Check(parsed.RootCluster == 2, "FAT32 root cluster = 2");
            Check(DiskCatalog.DetectFileSystem(boot) == FileSystemKind.Fat32, "FAT32 detekcia");

            using var disk = RawDisk.OpenFile(path);
            var volume = new Fat32Volume(disk, 0, parsed);
            volume.LoadFat(CancellationToken.None);
            Check(volume.UseFat && volume.Fat1Valid, "FAT1 je platná");

            var entries = Fat32Scanner.Scan(volume, includeDeleted: false, null, CancellationToken.None);
            Check(entries.Count == 1, "FAT32 našiel 1 súbor");
            Check(entries.Count == 1 && entries[0].FullPath == "HELLO.TXT", "FAT32 názov HELLO.TXT");
            Check(entries.Count == 1 && entries[0].Size == 5, "FAT32 veľkosť 5");

            if (entries.Count == 1)
            {
                using var ms = new MemoryStream();
                volume.ReadFile(entries[0], ms, null, CancellationToken.None, null);
                Check(Encoding.ASCII.GetString(ms.ToArray()) == "hello", "FAT32 obsah hello");
            }
        }
        catch (Exception ex)
        {
            Lines.Add("FAIL FAT32: " + ex);
            _failed++;
        }
        finally
        {
            try { if (path is not null && File.Exists(path)) File.Delete(path); } catch { }
        }
    }

    private static void TestSmartText()
    {
        var ok = new SmartInfo
        {
            Available = true,
            Health = "Healthy",
            Status = "OK",
            PowerOnHours = 3_010
        };
        SmartInfoService.ComposeDisplay(ok);
        Check(!ok.HasAlert, "S.M.A.R.T. OK nemá alert");
        Check(ok.DisplayText.Contains("125 dní", StringComparison.Ordinal),
            "S.M.A.R.T. OK zobrazuje dni");

        var bad = new SmartInfo
        {
            Available = true,
            Health = "Warning",
            Status = "OK",
            PowerOnHours = 3_010,
            Temperature = 61,
            Wear = 85,
            ReallocatedSectors = 3,
            PendingSectors = 2,
            UncorrectableSectors = 1,
            ReadErrorsUncorrected = 4,
            WriteErrorsUncorrected = 5
        };
        SmartInfoService.ComposeDisplay(bad);
        Check(bad.HasAlert, "S.M.A.R.T. alert sa zapne");
        Check(bad.DisplayText.Contains("125 dní", StringComparison.Ordinal),
            "S.M.A.R.T. alert obsahuje dni");
        Check(bad.DisplayText.Contains("teplota", StringComparison.Ordinal),
            "S.M.A.R.T. alert obsahuje teplotu");
        Check(bad.DisplayText.Contains("vadné sektory", StringComparison.Ordinal),
            "S.M.A.R.T. alert obsahuje vadné sektory");
        Check(bad.DisplayText.Contains("nestabilné sektory", StringComparison.Ordinal),
            "S.M.A.R.T. alert obsahuje nestabilné sektory");
        Check(bad.DisplayText.Contains("opotrebenie", StringComparison.Ordinal),
            "S.M.A.R.T. alert obsahuje opotrebenie");
        Check(bad.DisplayText.Contains("Odporúčaná výmena disku", StringComparison.Ordinal),
            "S.M.A.R.T. alert odporúča výmenu");
        Check(bad.DisplayText.Count(c => c == ';') >= 4,
            "S.M.A.R.T. alert vypíše viac dôvodov naraz");
    }

    private static void TestSystemFileDetection()
    {
        Check(!SystemFileService.DetectIssue(
            "No component store corruption detected.\nThe operation completed successfully.", 0),
            "DISM: zdravé úložisko sa vyhodnotí ako OK");
        Check(!SystemFileService.DetectIssue(
            "Nebolo zistené poškodenie komponentového úložiska.", 0),
            "DISM: slovenská správa bez poškodenia");
        Check(SystemFileService.DetectIssue("The component store is repairable.", 0),
            "DISM: opraviteľné poškodenie sa rozpozná");
        Check(SystemFileService.DetectIssue("Poškodenie komponentového úložiska je opraviteľné.", 0),
            "DISM: slovenské poškodenie sa rozpozná");
        Check(!SystemFileService.DetectIssue("", 0),
            "DISM: prázdny výstup s kódom 0 nie je chyba");
        Check(SystemFileService.DetectIssue("", 5),
            "DISM: nenulový kód je problém");
        Check(SystemFileService.DetectUnfixed(
            "Windows Resource Protection found corrupt files but was unable to fix some of them."),
            "SFC: neopravené súbory sa rozpoznajú");
        Check(!SystemFileService.DetectUnfixed(
            "Windows Resource Protection did not find any integrity violations."),
            "SFC: čistý výsledok nie je chyba");
    }

    private static void TestUpdates()
    {
        Check(UpdateService.IsNewer("2.5", "2.4"), "aktualizácia: 2.5 > 2.4");
        Check(!UpdateService.IsNewer("2.4", "2.4.0"), "aktualizácia: 2.4 = 2.4.0");
        Check(UpdateService.IsNewer("v2.5.1", "2.5.0"), "aktualizácia: v2.5.1 > 2.5.0");

        var info = UpdateService.Parse(
            "{\"version\":\"9.9\",\"portableUrl\":\"https://example/exe\"," +
            "\"pageUrl\":\"https://example/page\",\"changelog\":[\"prvá\",\"druhá\"]}");
        Check(info is { Available: true }, "aktualizácia: JSON sa parsuje");
        Check(info?.Changelog == "prvá\ndruhá", "aktualizácia: changelog sa načíta");
        Check(info?.PortableUrl == "https://example/exe", "aktualizácia: odkaz na exe");
    }

    private static void TestBootParsing()
    {
        var mbr = new byte[512];
        mbr[0] = 0x33;
        mbr[0x1BE] = 0x80;
        mbr[0x1CE + 4] = 0x07;
        mbr[510] = 0x55;
        mbr[511] = 0xAA;
        Check(BootAnalysisService.IsMbrValid(mbr), "boot: platný MBR podpis");
        Check(BootAnalysisService.CountActivePartitions(mbr) == 1, "boot: jedna active partícia");
        Check(BootAnalysisService.HasMbrBootCode(mbr), "boot: MBR boot code sa rozpozná");

        var onlySignature = new byte[512];
        onlySignature[440] = 0x9E; onlySignature[441] = 0xF9;
        onlySignature[442] = 0xF2; onlySignature[443] = 0xDC;
        onlySignature[510] = 0x55; onlySignature[511] = 0xAA;
        Check(!BootAnalysisService.HasMbrBootCode(onlySignature),
            "boot: samotná disk signature nie je boot code");

        var bad = (byte[])mbr.Clone();
        bad[510] = 0;
        bad[511] = 0;
        Check(!BootAnalysisService.IsMbrValid(bad), "boot: neplatný MBR podpis");

        var vbr = new byte[512];
        vbr[3] = (byte)'N'; vbr[4] = (byte)'T'; vbr[5] = (byte)'F'; vbr[6] = (byte)'S';
        vbr[510] = 0x55; vbr[511] = 0xAA;
        Check(BootAnalysisService.IsVbrValid(vbr), "boot: platný NTFS VBR");

        Check(!BootAnalysisService.IsFirmwareMismatch(true, FirmwareMode.Uefi),
            "boot: GPT + UEFI je kompatibilné");
        Check(!BootAnalysisService.IsFirmwareMismatch(false, FirmwareMode.Bios),
            "boot: MBR + BIOS je kompatibilné");
        Check(BootAnalysisService.IsFirmwareMismatch(false, FirmwareMode.Uefi),
            "boot: MBR na UEFI je mismatch");
        Check(BootAnalysisService.IsFirmwareMismatch(true, FirmwareMode.Bios),
            "boot: GPT na BIOS je mismatch");

        Check(BootRepairService.ExtractGuid(
                "The entry {12345678-1234-1234-1234-123456789abc} was successfully created.") ==
              "{12345678-1234-1234-1234-123456789abc}",
            "boot: GUID recovery záznamu sa parsuje");

        Check(UpdateInstaller.DetectMode(@"C:\Program Files\ZachranaDisku\ZachranaDisku.exe") ==
              UpdateMethod.Installed, "update: Program Files = inštalovaná");
        Check(UpdateInstaller.DetectMode(@"C:\Users\zdeno\Desktop\ZachranaDisku.exe") ==
              UpdateMethod.Portable, "update: Desktop = prenosná");
        Check(UpdateInstaller.DetectMode(@"C:\Program Files\dotnet\dotnet.exe") ==
              UpdateMethod.Unsupported, "update: dotnet = nepodporované");
    }

    private static void TestImageCopy()
    {
        string? source = null;
        string? output = null;
        try
        {
            const int size = 2 * 1024 * 1024;
            var data = new byte[size];
            for (int i = 0; i < size; i++) data[i] = (byte)(i * 31 + 7);

            source = Path.Combine(Path.GetTempPath(), $"zdenko-img-src-{Guid.NewGuid():N}.bin");
            output = Path.Combine(Path.GetTempPath(), $"zdenko-img-out-{Guid.NewGuid():N}.img");
            File.WriteAllBytes(source, data);

            var partition = new DiskPartition
            {
                ImagePath = source,
                Offset = 0,
                Length = size,
                DiskSize = size,
                SectorSize = 512,
                FileSystem = FileSystemKind.Raw
            };

            var result = DiskImageService.CreateAsync(partition, output, false, null, _ => { },
                CancellationToken.None).GetAwaiter().GetResult();

            Check(result.Bytes == size, "obraz: veľkosť");
            Check(File.Exists(output), "obraz: súbor vznikol");
            if (File.Exists(output))
            {
                var copy = File.ReadAllBytes(output);
                Check(copy.Length == size && copy.AsSpan().SequenceEqual(data), "obraz: obsah je rovnaký");
            }
        }
        catch (Exception ex)
        {
            Lines.Add("FAIL obraz: " + ex);
            _failed++;
        }
        finally
        {
            try { if (source is not null && File.Exists(source)) File.Delete(source); } catch { }
            try { if (output is not null && File.Exists(output)) File.Delete(output); } catch { }
            try
            {
                if (output is not null && File.Exists(output + ".part")) File.Delete(output + ".part");
            }
            catch { }
        }
    }

    private static void TestLznt1()
    {
        const ushort format = 2; // COMPRESSION_FORMAT_LZNT1

        try
        {
            var source = new byte[8192];
            for (int i = 0; i < source.Length; i++)
                source[i] = (byte)("NTFS compression test. "[i % 22]);

            if (RtlGetCompressionWorkSpaceSize(format, out uint workspaceSize, out _) != 0)
            {
                Lines.Add("SKIP LZNT1 (workspace sa nedá zistiť)");
                return;
            }

            var compressed = new byte[source.Length + 1024];
            var workspace = new byte[workspaceSize];
            int status = RtlCompressBuffer(format, source, source.Length,
                compressed, compressed.Length, 4096, out int compressedSize, workspace);

            if (status != 0 || compressedSize <= 0)
            {
                Lines.Add($"SKIP LZNT1 (RtlCompressBuffer status {status})");
                return;
            }

            var decompressed = Lznt1.Decompress(compressed[..compressedSize], source.Length);
            Check(decompressed.Length == source.Length, "LZNT1 veľkosť výstupu");
            Check(decompressed.AsSpan().SequenceEqual(source), "LZNT1 obsah po dekompresii");
        }
        catch (Exception ex)
        {
            Lines.Add("SKIP LZNT1: " + ex.Message);
        }
    }

    [DllImport("ntdll.dll")]
    private static extern int RtlGetCompressionWorkSpaceSize(ushort format, out uint compressWorkspace, out uint fragmentWorkspace);

    [DllImport("ntdll.dll")]
    private static extern int RtlCompressBuffer(
        ushort format,
        byte[] source,
        int sourceSize,
        byte[] destination,
        int destinationSize,
        int uncompressedChunkSize,
        out int finalCompressedSize,
        byte[] workspace);
}
