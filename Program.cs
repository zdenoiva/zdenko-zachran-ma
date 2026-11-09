using System.Security.Principal;
using System.Text;
using NtfsRescue.Core;
using NtfsRescue.Repair;
using NtfsRescue.Ui;
using NtfsRescue.Util;

namespace NtfsRescue;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            return SelfTest.Run();
        }

        if (args.Any(a => a.Equals("--catalog", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var parts = DiskCatalog.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                var lines = parts.Select(p =>
                    $"{p.Display} | disk={p.DiskNumber} part={p.PartitionNumber} " +
                    $"offset={p.Offset} length={p.Length} letter={p.DriveLetter} " +
                    $"type={p.PartitionType} system={p.IsSystem} model={p.Model}");
                var path = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-catalog.log");
                File.WriteAllLines(path, lines);
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-catalog.log"),
                    "CATALOG FAIL: " + ex);
                return 1;
            }
        }

        if (args.Any(a => a.Equals("--fixaccess", StringComparison.OrdinalIgnoreCase)))
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-fixaccess.log");
            try
            {
                if (!IsAdministrator())
                {
                    File.WriteAllText(logPath, "FIXACCESS FAIL: nie je administrátor");
                    return 2;
                }

                char letter = 'D';
                var letterArg = args.SkipWhile(a => !a.Equals("--fixaccess", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(letterArg) && char.IsLetter(letterArg[0]))
                    letter = char.ToUpperInvariant(letterArg[0]);

                var parts = DiskCatalog.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                var partition = parts.FirstOrDefault(p => !p.IsImage && p.HasLetter && p.Letter == letter);
                if (partition is null)
                {
                    File.WriteAllText(logPath, $"FIXACCESS FAIL: partícia {letter}: sa nenašla");
                    return 3;
                }

                void LogLine(string line)
                {
                    File.AppendAllText(logPath, line + Environment.NewLine);
                }

                File.WriteAllText(logPath, $"FIXACCESS {letter}: {partition.Display}\n");
                var result = VolumeAccessFixer.FixAsync(partition, false, LogLine,
                    null, CancellationToken.None).GetAwaiter().GetResult();
                File.AppendAllText(logPath, "\n" + result.Message + "\n" +
                    $"Mounted={result.Mounted} Accessible={result.Accessible}\n");
                return result.Accessible ? 0 : 1;
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, "FIXACCESS FAIL: " + ex);
                return 1;
            }
        }

        if (args.Any(a => a.Equals("--checkaccess", StringComparison.OrdinalIgnoreCase)))
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-checkaccess.log");
            try
            {
                char letter = 'D';
                var letterArg = args.SkipWhile(a => !a.Equals("--checkaccess", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(letterArg) && char.IsLetter(letterArg[0]))
                    letter = char.ToUpperInvariant(letterArg[0]);

                var parts = DiskCatalog.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                var p = parts.FirstOrDefault(x => !x.IsImage && x.HasLetter && x.Letter == letter);
                var ok = VolumeAccessFixer.HasUserAccess(letter);
                File.WriteAllText(logPath,
                    $"CHECKACCESS {letter}: found={p is not null} userAccess={ok}\n" +
                    (p is null ? "" : p.Display + "\n"));
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, "CHECKACCESS FAIL: " + ex);
                return 1;
            }
        }

        if (args.Any(a => a.Equals("--smart", StringComparison.OrdinalIgnoreCase)))
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-smart-check.log");
            try
            {
                char letter = 'D';
                var letterArg = args.SkipWhile(a => !a.Equals("--smart", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(letterArg) && char.IsLetter(letterArg[0]))
                    letter = char.ToUpperInvariant(letterArg[0]);

                var parts = DiskCatalog.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                var p = parts.FirstOrDefault(x => !x.IsImage && x.HasLetter && x.Letter == letter);
                if (p is null)
                {
                    File.WriteAllText(logPath, "SMART FAIL: partícia sa nenašla");
                    return 3;
                }
                var smart = SmartInfoService.Query(p.DiskNumber, CancellationToken.None);
                File.WriteAllText(logPath,
                    $"SMART {letter}: {smart.DisplayText}\n" +
                    $"Available={smart.Available} Alert={smart.HasAlert} Hours={smart.PowerOnHours} " +
                    $"Temp={smart.Temperature} Wear={smart.Wear} Model={smart.Model}\n");
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, "SMART FAIL: " + ex);
                return 1;
            }
        }

        if (args.Any(a => a.Equals("--checkenv", StringComparison.OrdinalIgnoreCase)))
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-checkenv.log");
            try
            {
                var lines = new List<string>();
                var items = PrerequisiteChecker.CheckAsync(lines.Add, CancellationToken.None)
                    .GetAwaiter().GetResult();
                foreach (var item in items)
                {
                    lines.Add($"[{(item.Ok ? "OK" : "CHYBA")}] {item.Title}: {item.Detail}");
                }
                lines.Add("REQUIRED_MISSING=" + items.Count(i => i.Required && !i.Ok));
                File.WriteAllLines(logPath, lines);
                return items.All(i => i.Ok || !i.Required) ? 0 : 1;
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, "CHECKENV FAIL: " + ex);
                return 2;
            }
        }

        if (args.Any(a => a.Equals("--diagnose", StringComparison.OrdinalIgnoreCase)))
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-diagnose.log");
            try
            {
                char letter = 'D';
                var letterArg = args.SkipWhile(a => !a.Equals("--diagnose", StringComparison.OrdinalIgnoreCase))
                    .Skip(1).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(letterArg) && char.IsLetter(letterArg[0]))
                    letter = char.ToUpperInvariant(letterArg[0]);

                var parts = DiskCatalog.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                var p = parts.FirstOrDefault(x => !x.IsImage && x.HasLetter && x.Letter == letter);
                if (p is null)
                {
                    File.WriteAllText(logPath, $"DIAGNOSE {letter}: partícia sa nenašla");
                    return 3;
                }
                var report = QuickDiagnosisService.RunAsync(p, CancellationToken.None)
                    .GetAwaiter().GetResult();
                var text = new StringBuilder();
                text.AppendLine($"DIAGNOSE {letter}: {p.Display}");
                text.AppendLine($"severity={report.Severity} title={report.Title}");
                text.AppendLine("details=" + report.Details.Replace("\r", "").Replace("\n", " | "));
                text.AppendLine("actions=" + string.Join(",", report.Actions));
                File.WriteAllText(logPath, text.ToString());
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, "DIAGNOSE FAIL: " + ex);
                return 1;
            }
        }

        if (args.Any(a => a.Equals("--recoverycancel", StringComparison.OrdinalIgnoreCase)))
        {
            ApplicationConfiguration.Initialize();
            var logPath = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-recoverycancel.log");
            try
            {
                using var form = new MainForm(smoke: true) { ShowInTaskbar = false };
                form.Show();
                var task = form.TestRecoveryCancelAsync(new SlowRecoverySource());
                var deadline = DateTime.UtcNow.AddSeconds(20);
                while (!task.IsCompleted && DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(20);
                }
                var text = task.IsCompleted
                    ? task.GetAwaiter().GetResult()
                    : "TIMEOUT (načítanie sa nepodarilo zrušiť včas)";
                File.WriteAllText(logPath, text);
                form.Close();
                return text.Contains("closed=True", StringComparison.Ordinal) &&
                       text.Contains("busyAfter=False", StringComparison.Ordinal) ? 0 : 1;
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, "RECOVERYCANCEL FAIL: " + ex);
                return 1;
            }
        }

        if (args.Any(a => a.Equals("--uishot", StringComparison.OrdinalIgnoreCase)))
        {
            ApplicationConfiguration.Initialize();
            var path = args.SkipWhile(a => !a.Equals("--uishot", StringComparison.OrdinalIgnoreCase))
                .Skip(1).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(path))
                path = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-ui.png");
            try
            {
                var loadDisks = args.Any(a => a.Equals("--loaddisks", StringComparison.OrdinalIgnoreCase));
                using var form = new MainForm(smoke: !loadDisks)
                {
                    Opacity = 1,
                    ShowInTaskbar = false
                };
                form.Show();
                if (args.Any(a => a.Equals("--diagtab", StringComparison.OrdinalIgnoreCase)))
                    form.SelectTabForTest(1);
                if (args.Any(a => a.Equals("--boottab", StringComparison.OrdinalIgnoreCase)))
                    form.SelectTabForTest(2);
                if (args.Any(a => a.Equals("--disktab", StringComparison.OrdinalIgnoreCase)))
                    form.SelectTabForTest(3);
                if (args.Any(a => a.Equals("--surfacetab", StringComparison.OrdinalIgnoreCase)))
                    form.SelectTabForTest(4);
                if (args.Any(a => a.Equals("--updatebanner", StringComparison.OrdinalIgnoreCase)))
                {
                    if (loadDisks &&
                        !args.Any(a => a.Equals("--bannerearly", StringComparison.OrdinalIgnoreCase)))
                    {
                        for (int i = 0; i < 90; i++)
                        {
                            Application.DoEvents();
                            Thread.Sleep(50);
                        }
                    }
                    form.ShowUpdateBannerForTest("9.9");
                }
                var loops = args.Any(a => a.Equals("--boottab", StringComparison.OrdinalIgnoreCase)) ? 140
                    : args.Any(a => a.Equals("--disktab", StringComparison.OrdinalIgnoreCase)) ? 140
                    : args.Any(a => a.Equals("--surfacetab", StringComparison.OrdinalIgnoreCase)) ? 140
                    : loadDisks ? 200 : 40;
                for (int i = 0; i < loops; i++)
                {
                    Application.DoEvents();
                    Thread.Sleep(50);
                }
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                bitmap.Save(path);
                form.Close();
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(path + ".error.txt", ex.ToString());
                return 1;
            }
        }

        if (args.Any(a => a.Equals("--uishotpre", StringComparison.OrdinalIgnoreCase)))
        {
            ApplicationConfiguration.Initialize();
            var path = args.SkipWhile(a => !a.Equals("--uishotpre", StringComparison.OrdinalIgnoreCase))
                .Skip(1).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(path))
                path = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-prereq.png");
            try
            {
                using var form = new PrerequisitesForm(firstRun: true);
                form.Show();
                for (int i = 0; i < 100; i++)
                {
                    Application.DoEvents();
                    Thread.Sleep(50);
                }
                using var bitmap = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                bitmap.Save(path);
                var tree = new List<string>();
                Walk(form, 0, tree);
                File.WriteAllLines(path + ".tree.txt", tree);
                form.Close();
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(path + ".error.txt", ex.ToString());
                return 1;
            }
        }

        if (args.Any(a => a.Equals("--bootcheck", StringComparison.OrdinalIgnoreCase)))
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-bootcheck.log");
            try
            {
                char letter = 'D';
                var letterArg = args.SkipWhile(a => !a.Equals("--bootcheck", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(letterArg) && char.IsLetter(letterArg[0]))
                    letter = char.ToUpperInvariant(letterArg[0]);

                var parts = DiskCatalog.LoadAllAsync(CancellationToken.None).GetAwaiter().GetResult();
                var p = parts.FirstOrDefault(x => !x.IsImage && x.HasLetter && x.Letter == letter) ??
                        parts.FirstOrDefault(x => !x.IsImage && x.IsSystem);
                if (p is null)
                {
                    File.WriteAllText(logPath, "BOOTCHECK FAIL: partícia sa nenašla");
                    return 3;
                }

                var lines = new List<string>();
                var a = BootAnalysisService.AnalyzeDiskAsync(p, parts, lines.Add, null,
                    CancellationToken.None).GetAwaiter().GetResult();
                lines.Add($"Disk={a.Target.DiskNumber} style={a.Target.PartitionStyle} firmware={a.Firmware} " +
                          $"bootMode={a.BootMode} system={a.Target.DiskIsSystem} canRepair={a.CanRepair} " +
                          $"canConvert={a.CanConvert} windows={a.WindowsPartitions.Count} " +
                          $"esp={a.Esp?.PartitionNumber} active={a.ActivePartition?.PartitionNumber} " +
                          $"summary={a.Summary}");
                foreach (var c in a.Checks)
                    lines.Add($"[{c.Level}] {c.Name}: {c.Detail}");
                File.WriteAllLines(logPath, lines);
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, "BOOTCHECK FAIL: " + ex);
                return 1;
            }
        }

        if (args.Any(a => a.Equals("--bootrepair", StringComparison.OrdinalIgnoreCase)))
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-bootrepair.log");
            try
            {
                char letter = 'D';
                var letterArg = args.SkipWhile(a => !a.Equals("--bootrepair", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(letterArg) && char.IsLetter(letterArg[0]))
                    letter = char.ToUpperInvariant(letterArg[0]);
                var parts = DiskCatalog.LoadAllAsync(CancellationToken.None).GetAwaiter().GetResult();
                var p = parts.FirstOrDefault(x => !x.IsImage && x.HasLetter && x.Letter == letter);
                if (p is null) { File.WriteAllText(logPath, "BOOTREPAIR FAIL: partícia sa nenašla"); return 3; }
                var lines = new List<string>();
                var analysis = BootAnalysisService.AnalyzeDiskAsync(p, parts, lines.Add, null,
                    CancellationToken.None).GetAwaiter().GetResult();
                var renew = args.Any(a => a.Equals("--renew-signature", StringComparison.OrdinalIgnoreCase));
                var result = BootRepairService.RepairAsync(analysis, renew, lines.Add, null,
                    CancellationToken.None).GetAwaiter().GetResult();
                lines.Add($"Success={result.Success} Backup={result.BackupFolder}");
                lines.Add(result.Summary);
                File.WriteAllLines(logPath, lines);
                return result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, "BOOTREPAIR FAIL: " + ex);
                return 1;
            }
        }

        if (args.Any(a => a.Equals("--bootconvert", StringComparison.OrdinalIgnoreCase)))
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-bootconvert.log");
            try
            {
                char letter = 'D';
                var letterArg = args.SkipWhile(a => !a.Equals("--bootconvert", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(letterArg) && char.IsLetter(letterArg[0]))
                    letter = char.ToUpperInvariant(letterArg[0]);
                var parts = DiskCatalog.LoadAllAsync(CancellationToken.None).GetAwaiter().GetResult();
                var p = parts.FirstOrDefault(x => !x.IsImage && x.HasLetter && x.Letter == letter);
                if (p is null) { File.WriteAllText(logPath, "BOOTCONVERT FAIL: partícia sa nenašla"); return 3; }
                var lines = new List<string>();
                var analysis = BootAnalysisService.AnalyzeDiskAsync(p, parts, lines.Add, null,
                    CancellationToken.None).GetAwaiter().GetResult();
                var result = BootRepairService.ConvertMbrToGptAsync(analysis, lines.Add, null,
                    CancellationToken.None).GetAwaiter().GetResult();
                lines.Add($"Success={result.Success} Backup={result.BackupFolder}");
                lines.Add(result.Summary);
                File.WriteAllLines(logPath, lines);
                return result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, "BOOTCONVERT FAIL: " + ex);
                return 1;
            }
        }

        if (args.Any(a => a.Equals("--bootrecovery", StringComparison.OrdinalIgnoreCase)) ||
            args.Any(a => a.Equals("--bootoem", StringComparison.OrdinalIgnoreCase)))
        {
            var oem = args.Any(a => a.Equals("--bootoem", StringComparison.OrdinalIgnoreCase));
            var switchName = oem ? "--bootoem" : "--bootrecovery";
            var logPath = Path.Combine(Path.GetTempPath(), oem
                ? "ZdenkoZachranMa-bootoem.log"
                : "ZdenkoZachranMa-bootrecovery.log");
            try
            {
                char letter = 'D';
                var letterArg = args.SkipWhile(a => !a.Equals(switchName, StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(letterArg) && char.IsLetter(letterArg[0]))
                    letter = char.ToUpperInvariant(letterArg[0]);
                var parts = DiskCatalog.LoadAllAsync(CancellationToken.None).GetAwaiter().GetResult();
                var p = parts.FirstOrDefault(x => !x.IsImage && x.HasLetter && x.Letter == letter);
                if (p is null) { File.WriteAllText(logPath, "BOOTRECOVERY FAIL: partícia sa nenašla"); return 3; }
                var lines = new List<string>();
                var analysis = BootAnalysisService.AnalyzeDiskAsync(p, parts, lines.Add, null,
                    CancellationToken.None).GetAwaiter().GetResult();
                var image = oem ? analysis.OemImage : analysis.WinReImage;
                if (image is null) { File.WriteAllText(logPath, "BOOTRECOVERY FAIL: obraz sa nenašiel"); return 4; }
                var result = BootRepairService.AddRecoveryAsync(analysis, image, lines.Add, null,
                    CancellationToken.None).GetAwaiter().GetResult();
                lines.Add($"Success={result.Success} Backup={result.BackupFolder} Guid={result.EntryGuid}");
                lines.Add(result.Summary);
                File.WriteAllLines(logPath, lines);
                return result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, "BOOTRECOVERY FAIL: " + ex);
                return 1;
            }
        }

        if (args.Any(a => a.Equals("--surfacescan", StringComparison.OrdinalIgnoreCase)))
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-surfacescan.log");
            try
            {
                var numberArg = args.SkipWhile(a => !a.Equals("--surfacescan", StringComparison.OrdinalIgnoreCase))
                    .Skip(1).FirstOrDefault();
                if (!int.TryParse(numberArg, out int diskNumber))
                {
                    File.WriteAllText(logPath, "SURFACESCAN FAIL: chýba číslo disku");
                    return 3;
                }
                var disks = DiskInfoCatalog.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                var disk = disks.FirstOrDefault(d => d.Number == diskNumber);
                if (disk is null) { File.WriteAllText(logPath, "SURFACESCAN FAIL: disk sa nenašiel"); return 4; }
                var lines = new List<string>();
                var result = SurfaceScanner.Scan(disk, null, lines.Add, CancellationToken.None);
                var report = Path.Combine(Path.GetTempPath(), $"povrch-disk-{diskNumber}.csv");
                result.SaveReport(report);
                lines.Add($"Success=True Report={report}");
                File.WriteAllLines(logPath, lines);
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, "SURFACESCAN FAIL: " + ex);
                return 1;
            }
        }

        if (args.Any(a => a.Equals("--surfacerepair", StringComparison.OrdinalIgnoreCase)))
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-surfacerepair.log");
            try
            {
                var rest = args.SkipWhile(a => !a.Equals("--surfacerepair", StringComparison.OrdinalIgnoreCase))
                    .Skip(1).ToList();
                if (rest.Count < 2 || !int.TryParse(rest[0], out int diskNumber) ||
                    !long.TryParse(rest[1], out long offset))
                {
                    File.WriteAllText(logPath, "SURFACEREPAIR FAIL: chýba disk/offset");
                    return 3;
                }
                var disks = DiskInfoCatalog.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                var disk = disks.FirstOrDefault(d => d.Number == diskNumber);
                if (disk is null) { File.WriteAllText(logPath, "SURFACEREPAIR FAIL: disk sa nenašiel"); return 4; }
                var lines = new List<string>();
                var result = SurfaceRepairService.RepairAsync(disk, new[] { offset }, null,
                    lines.Add, CancellationToken.None).GetAwaiter().GetResult();
                lines.Add($"Repaired={result.Repaired} Failed={result.Failed} Backup={result.BackupPath}");
                lines.Add(result.Summary);
                File.WriteAllLines(logPath, lines);
                return result.Failed == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, "SURFACEREPAIR FAIL: " + ex);
                return 1;
            }
        }

        if (args.Any(a => a.Equals("--diskerase", StringComparison.OrdinalIgnoreCase)))
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-diskerase.log");
            try
            {
                var numberArg = args.SkipWhile(a => !a.Equals("--diskerase", StringComparison.OrdinalIgnoreCase))
                    .Skip(1).FirstOrDefault();
                if (!int.TryParse(numberArg, out int diskNumber))
                {
                    File.WriteAllText(logPath, "DISKERASE FAIL: chýba číslo disku");
                    return 3;
                }

                var disks = DiskInfoCatalog.LoadAsync(CancellationToken.None).GetAwaiter().GetResult();
                var disk = disks.FirstOrDefault(d => d.Number == diskNumber);
                if (disk is null)
                {
                    File.WriteAllText(logPath, "DISKERASE FAIL: disk sa nenašiel");
                    return 4;
                }

                var letterArg = args.SkipWhile(a => !a.Equals("--letter", StringComparison.OrdinalIgnoreCase))
                    .Skip(1).FirstOrDefault();
                var labelArg = args.SkipWhile(a => !a.Equals("--label", StringComparison.OrdinalIgnoreCase))
                    .Skip(1).FirstOrDefault();
                var options = new EraseOptions
                {
                    FullWipe = args.Any(a => a.Equals("--full", StringComparison.OrdinalIgnoreCase)),
                    CreatePartition = !args.Any(a => a.Equals("--no-partition", StringComparison.OrdinalIgnoreCase)),
                    FileSystem = args.Any(a => a.Equals("--fat32", StringComparison.OrdinalIgnoreCase)) ? "FAT32" : "NTFS",
                    PartitionStyle = args.Any(a => a.Equals("--mbr", StringComparison.OrdinalIgnoreCase)) ? "MBR" : "GPT",
                    Letter = !string.IsNullOrWhiteSpace(letterArg) && char.IsLetter(letterArg[0])
                        ? char.ToUpperInvariant(letterArg[0])
                        : null,
                    Label = string.IsNullOrWhiteSpace(labelArg) ? "DATA" : labelArg
                };

                var lines = new List<string>();
                var result = DiskEraser.EraseAsync(disk, options, null, lines.Add, CancellationToken.None)
                    .GetAwaiter().GetResult();
                lines.Add($"Success={result.Success} Drive={result.DriveLetter}");
                lines.Add(result.Summary);
                File.WriteAllLines(logPath, lines);
                return result.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, "DISKERASE FAIL: " + ex);
                return 1;
            }
        }

        if (args.Any(a => a.Equals("--selfupdate", StringComparison.OrdinalIgnoreCase)))
        {
            var logPath = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-selfupdate.log");
            try
            {
                var info = UpdateService.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();
                if (info is null)
                {
                    File.WriteAllText(logPath, "SELFUPDATE FAIL: feed sa nenačítal");
                    return 2;
                }
                File.WriteAllText(logPath,
                    $"latest={info.Version} current={AppInfo.VersionDisplay} available={info.Available}\n");
                if (!info.Available)
                    return 0;

                var result = UpdateInstaller.ApplyAsync(info, null, CancellationToken.None)
                    .GetAwaiter().GetResult();
                File.AppendAllText(logPath,
                    $"method={result.Method} exitNow={result.ExitNow}\n{result.Message}\n");
                return result.ExitNow ? 0 : 1;
            }
            catch (Exception ex)
            {
                File.WriteAllText(logPath, "SELFUPDATE FAIL: " + ex);
                return 1;
            }
        }

        if (args.Any(a => a.Equals("--uismoke", StringComparison.OrdinalIgnoreCase)))
        {
            ApplicationConfiguration.Initialize();
            var log = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-uismoke.log");
            try
            {
                using (var prerequisites = new PrerequisitesForm(firstRun: true))
                {
                    prerequisites.CreateControl();
                }

                using var form = new MainForm(smoke: true)
                {
                    Opacity = 0,
                    ShowInTaskbar = false,
                    WindowState = FormWindowState.Minimized
                };
                form.Show();
                Application.DoEvents();
                form.Close();
                File.WriteAllText(log, "UISMOKE OK");
                return 0;
            }
            catch (Exception ex)
            {
                File.WriteAllText(log, "UISMOKE FAIL: " + ex);
                return 1;
            }
        }

        Directory.CreateDirectory(AppPaths.Logs);
        Log.FilePath = Path.Combine(AppPaths.Logs, $"session-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        Log.Write($"{AppInfo.Name} start, verzia {typeof(Program).Assembly.GetName().Version}");

        if (!IsAdministrator())
        {
            MessageBox.Show(
                AppInfo.Name + " potrebuje spravu ako administrátor.\n\n" +
                "Spustite ho pravým kliknutím -> Spustiť ako správca.",
                AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            Log.Error("Neošetrená chyba: " + e.Exception);
            MessageBox.Show(e.Exception.Message, AppInfo.Name + " - chyba",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Log.Error("Fatálna chyba: " + ex);
        };

        try
        {
            AppSettings.Load();
            if (!string.Equals(AppSettings.EnvironmentCheckedFor, AppInfo.Version,
                    StringComparison.OrdinalIgnoreCase))
            {
                List<PrerequisiteItem>? items = null;
                try
                {
                    items = PrerequisiteChecker.CheckAsync(
                        message => Log.Write("Kontrola prostredia: " + message),
                        CancellationToken.None).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log.Warn("Kontrola prostredia zlyhala: " + ex.Message);
                }

                var missing = items?.Where(i => i.Required && !i.Ok).ToList() ?? new List<PrerequisiteItem>();
                var environmentOk = items is not null && missing.Count == 0;
                if (environmentOk)
                {
                    Log.Info("Kontrola prostredia: všetko je v poriadku.");
                }
                else if (missing.Count > 0)
                {
                    Log.Warn($"Kontrola prostredia: chýba {missing.Count} povinných položiek, spúšťam automatickú opravu.");
                    using var check = new PrerequisitesForm(firstRun: true, autoInstall: true,
                        preloaded: items);
                    check.ShowDialog();
                    environmentOk = check.AllRequiredOk;
                }

                if (environmentOk)
                {
                    AppSettings.EnvironmentCheckedFor = AppInfo.Version;
                    AppSettings.Save();
                }
                else
                {
                    Log.Warn("Kontrola prostredia sa nedokončila úplne; pri ďalšom spustení sa skúsi znova.");
                }
            }

            Application.Run(new MainForm());
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("Spustenie zlyhalo: " + ex);
            MessageBox.Show(ex.Message, AppInfo.Name + " - chyba",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void Walk(Control parent, int depth, List<string> lines)
    {
        foreach (Control c in parent.Controls)
        {
            var text = c is Label or Button ? (c.Text ?? "").Replace("\n", " ") : "";
            lines.Add(new string(' ', depth * 2) +
                      $"{c.GetType().Name} bounds={c.Bounds} visible={c.Visible} text=\"{text}\"");
            if (depth < 8) Walk(c, depth + 1, lines);
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Pomalý zdroj pre test zrušenia načítavania v zálohe Záchrana súborov.</summary>
    private sealed class SlowRecoverySource : IRecoverySource
    {
        public string FileSystemName => "TEST";
        public string SourceName => "testovací zdroj";
        public int DiskNumber => -1;
        public long TotalBytes => 0;
        public List<RecoveryEntry> Entries { get; } = new();

        public void Load(bool includeDeleted, IProgress<ScanProgress>? progress, CancellationToken ct)
        {
            for (int i = 0; i < 100; i++)
            {
                ct.ThrowIfCancellationRequested();
                Thread.Sleep(50);
                progress?.Report(new ScanProgress(i, 100, "Test"));
            }
        }

        public void CopyFile(RecoveryEntry entry, Stream output, Action<long>? progress,
            CancellationToken ct, Action<string>? warn) => throw new NotSupportedException();

        public void Dispose() { }
    }
}
