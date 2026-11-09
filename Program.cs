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

        if (args.Any(a => a.Equals("--uishot", StringComparison.OrdinalIgnoreCase)))
        {
            ApplicationConfiguration.Initialize();
            var path = args.SkipWhile(a => !a.Equals("--uishot", StringComparison.OrdinalIgnoreCase))
                .Skip(1).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(path))
                path = Path.Combine(Path.GetTempPath(), "ZdenkoZachranMa-ui.png");
            try
            {
                using var form = new MainForm(smoke: true)
                {
                    Opacity = 1,
                    ShowInTaskbar = false
                };
                form.Show();
                if (args.Any(a => a.Equals("--diagtab", StringComparison.OrdinalIgnoreCase)))
                    form.SelectTabForTest(1);
                if (args.Any(a => a.Equals("--boottab", StringComparison.OrdinalIgnoreCase)))
                    form.SelectTabForTest(2);
                if (args.Any(a => a.Equals("--updatebanner", StringComparison.OrdinalIgnoreCase)))
                    form.ShowUpdateBannerForTest("9.9");
                var loops = args.Any(a => a.Equals("--boottab", StringComparison.OrdinalIgnoreCase)) ? 140 : 40;
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
}
