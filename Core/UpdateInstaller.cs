using System.Diagnostics;
using System.Text;
using NtfsRescue.Util;

namespace NtfsRescue.Core;

public enum UpdateMethod { Unsupported, Portable, Installed }

public sealed class UpdateLaunchResult
{
    public bool ExitNow;
    public UpdateMethod Method;
    public string Message = "";
}

public static class UpdateInstaller
{
    public static string CurrentExePath => Environment.ProcessPath ?? "";

    public static UpdateMethod DetectMode()
        => DetectMode(CurrentExePath);

    internal static UpdateMethod DetectMode(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Path.GetFileName(path).Equals("ZachranaDisku.exe", StringComparison.OrdinalIgnoreCase))
            return UpdateMethod.Unsupported;

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");

        if (IsUnder(path, programFiles) || IsUnder(path, programFilesX86) ||
            IsUnder(path, localPrograms))
            return UpdateMethod.Installed;

        return UpdateMethod.Portable;
    }

    public static async Task<UpdateLaunchResult> ApplyAsync(UpdateInfo info,
        IProgress<double>? progress, CancellationToken ct)
    {
        var mode = DetectMode();
        if (mode == UpdateMethod.Unsupported)
            throw new InvalidOperationException("Automatická aktualizácia nie je dostupná v tomto režime.");

        if (mode == UpdateMethod.Installed)
        {
            if (string.IsNullOrWhiteSpace(info.InstallerUrl))
                throw new InvalidOperationException("Chýba odkaz na inštalátor.");
            var setup = Path.Combine(Path.GetTempPath(),
                $"ZachranaDisku-Setup-{info.Version}.exe");
            await UpdateService.DownloadAsync(info.InstallerUrl, setup, info.InstallerSha256,
                progress, ct).ConfigureAwait(false);

            // Inštalátor sa spustí až po ukončení programu; inak by nemohol prepísať
            // bežiaci ZachranaDisku.exe a ohlásil by chybu kopírovania.
            var installScript = Path.Combine(Path.GetTempPath(),
                $"zzm-install-{Guid.NewGuid():N}.ps1");
            File.WriteAllText(installScript, InstallHelperScript, new UTF8Encoding(false));

            var installHelper = new ProcessStartInfo(PowerShellPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            installHelper.ArgumentList.Add("-NoProfile");
            installHelper.ArgumentList.Add("-ExecutionPolicy");
            installHelper.ArgumentList.Add("Bypass");
            installHelper.ArgumentList.Add("-File");
            installHelper.ArgumentList.Add(installScript);
            installHelper.ArgumentList.Add("-AppPid");
            installHelper.ArgumentList.Add(Environment.ProcessId.ToString());
            installHelper.ArgumentList.Add("-Setup");
            installHelper.ArgumentList.Add(setup);
            installHelper.ArgumentList.Add("-WorkDir");
            installHelper.ArgumentList.Add(Path.GetTempPath());

            Process.Start(installHelper);
            return new UpdateLaunchResult
            {
                ExitNow = true,
                Method = mode,
                Message = "Inštalátor sa spustí po zavretí programu a nainštaluje novú verziu."
            };
        }

        if (string.IsNullOrWhiteSpace(info.PortableUrl))
            throw new InvalidOperationException("Chýba odkaz na prenosnú verziu.");

        var target = CurrentExePath;
        var targetDir = Path.GetDirectoryName(target) ?? Path.GetTempPath();
        EnsureWritable(targetDir);

        var downloaded = Path.Combine(Path.GetTempPath(),
            $"ZachranaDisku-{info.Version}-{Guid.NewGuid():N}.exe");
        await UpdateService.DownloadAsync(info.PortableUrl, downloaded, info.Sha256,
            progress, ct).ConfigureAwait(false);

        var script = Path.Combine(Path.GetTempPath(),
            $"zzm-update-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(script, HelperScript, new UTF8Encoding(false));

        var helper = new ProcessStartInfo(PowerShellPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        helper.ArgumentList.Add("-NoProfile");
        helper.ArgumentList.Add("-ExecutionPolicy");
        helper.ArgumentList.Add("Bypass");
        helper.ArgumentList.Add("-File");
        helper.ArgumentList.Add(script);
        helper.ArgumentList.Add("-AppPid");
        helper.ArgumentList.Add(Environment.ProcessId.ToString());
        helper.ArgumentList.Add("-Source");
        helper.ArgumentList.Add(downloaded);
        helper.ArgumentList.Add("-Target");
        helper.ArgumentList.Add(target);
        helper.ArgumentList.Add("-WorkDir");
        helper.ArgumentList.Add(targetDir);

        Process.Start(helper);
        return new UpdateLaunchResult
        {
            ExitNow = true,
            Method = mode,
            Message = "Prenosná verzia sa stiahla. Program sa zavrie, prepíše sa a spustí sa nová verzia."
        };
    }

    private static string PowerShellPath =>
        Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    private static bool IsUnder(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(root)) return false;
        try
        {
            var full = Path.GetFullPath(path);
            var prefix = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureWritable(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".zzm-write-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "test");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Priečinok s programom nie je zapisovateľný. Skopírujte program do zapisovateľného priečinka.", ex);
        }
    }

    private const string InstallHelperScript = """
param(
  [int]$AppPid,
  [string]$Setup,
  [string]$WorkDir
)
$ErrorActionPreference = 'Stop'
$log = Join-Path $env:TEMP 'zzm-update-error.log'
try {
  $deadline = (Get-Date).AddSeconds(120)
  while ((Get-Process -Id $AppPid -ErrorAction SilentlyContinue) -and ((Get-Date) -lt $deadline)) {
    Start-Sleep -Milliseconds 250
  }
  Start-Sleep -Milliseconds 500
  Start-Process -FilePath $Setup -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CLOSEAPPLICATIONS','/RESTARTAPPLICATIONS','/AUTOUPDATE' -WorkingDirectory $WorkDir -Wait
}
catch {
  ("Aktualizácia zlyhala: " + $_.Exception.Message) | Out-File -FilePath $log -Encoding utf8
}
finally {
  Remove-Item -LiteralPath $Setup -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
}
""";

    private const string HelperScript = """
param(
  [int]$AppPid,
  [string]$Source,
  [string]$Target,
  [string]$WorkDir
)
$ErrorActionPreference = 'Stop'
$log = Join-Path $env:TEMP 'zzm-update-error.log'
try {
  $deadline = (Get-Date).AddSeconds(120)
  while ((Get-Process -Id $AppPid -ErrorAction SilentlyContinue) -and ((Get-Date) -lt $deadline)) {
    Start-Sleep -Milliseconds 250
  }
  Start-Sleep -Milliseconds 500
  Copy-Item -LiteralPath $Source -Destination $Target -Force
  Unblock-File -LiteralPath $Target -ErrorAction SilentlyContinue
  Start-Process -FilePath $Target -WorkingDirectory $WorkDir
}
catch {
  ("Aktualizácia zlyhala: " + $_.Exception.Message) | Out-File -FilePath $log -Encoding utf8
}
finally {
  Remove-Item -LiteralPath $Source -Force -ErrorAction SilentlyContinue
  Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
}
""";
}
