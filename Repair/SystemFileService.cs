using System.Text;
using NtfsRescue.Util;

namespace NtfsRescue.Repair;

public sealed class SystemFileResult
{
    public bool IssueFound;
    public bool Success;
    public bool RebootRequired;
    public string Summary = "";
    public string Output = "";
}

public static class SystemFileService
{
    private static string Dism => Path.Combine(Environment.SystemDirectory, "dism.exe");
    private static string Sfc => Path.Combine(Environment.SystemDirectory, "sfc.exe");

    /// <summary>Rýchla kontrola komponentového úložiska Windows (DISM CheckHealth).</summary>
    public static async Task<SystemFileResult> CheckHealthAsync(Action<string> log, CancellationToken ct)
    {
        log("DISM /Online /Cleanup-Image /CheckHealth");
        log("Kontrola môže trvať niekoľko sekúnd až minút…");

        var result = await ProcessRunner.RunAsync(Dism,
            new[] { "/Online", "/Cleanup-Image", "/CheckHealth" },
            log, ct).ConfigureAwait(false);

        var issue = DetectIssue(result.Output, result.ExitCode);
        var summary = issue
            ? "DISM zistil poškodenie systémových súborov alebo komponentového úložiska."
            : result.ExitCode == 0
                ? "DISM nenašiel poškodenie systémových súborov."
                : $"DISM skončil s kódom {result.ExitCode}. Skontrolujte protokol.";

        return new SystemFileResult
        {
            IssueFound = issue,
            Success = result.ExitCode == 0 && !issue,
            RebootRequired = result.ExitCode == 3010,
            Summary = summary,
            Output = result.Output
        };
    }

    /// <summary>Oprava: DISM RestoreHealth + SFC /scannow.</summary>
    public static async Task<SystemFileResult> RepairAsync(Action<string> log,
        IProgress<string>? phase, CancellationToken ct)
    {
        phase?.Report("DISM RestoreHealth");
        log("");
        log("DISM /Online /Cleanup-Image /RestoreHealth");
        log("Oprava komponentového úložiska môže trvať dlho a môže vyžadovať internet…");
        var restore = await ProcessRunner.RunAsync(Dism,
            new[] { "/Online", "/Cleanup-Image", "/RestoreHealth" },
            log, ct).ConfigureAwait(false);

        phase?.Report("SFC /scannow");
        log("");
        log("SFC /scannow");
        log("Kontrola a oprava chránených systémových súborov…");
        var sfc = await ProcessRunner.RunAsync(Sfc,
            new[] { "/scannow" }, log, ct).ConfigureAwait(false);

        var restoreOk = restore.ExitCode is 0 or 3010;
        var sfcOk = sfc.ExitCode is 0 or 3010;
        var sfcUnfixed = DetectUnfixed(sfc.Output);
        var reboot = restore.ExitCode == 3010 || sfc.ExitCode == 3010;
        var success = restoreOk && sfcOk && !sfcUnfixed;

        var summary = success
            ? "Systémové súbory boli skontrolované a opravené." +
              (reboot ? " Po dokončení je potrebný reštart počítača." : "")
            : "Oprava systémových súborov sa nedokončila úplne. Pozrite protokol nižšie." +
              (reboot ? " Po dokončení je potrebný reštart počítača." : "");

        return new SystemFileResult
        {
            IssueFound = !success,
            Success = success,
            RebootRequired = reboot,
            Summary = summary,
            Output = restore.Output + Environment.NewLine + sfc.Output
        };
    }

    internal static bool DetectIssue(string output, int exitCode)
    {
        var text = Normalize(output);

        // Najprv vylúčime jasné správy, že poškodenie nebolo zistené.
        if (text.Contains("no component store corruption") ||
            text.Contains("corruption was not detected") ||
            text.Contains("no corruption") ||
            text.Contains("ziadne poskodenie") ||
            text.Contains("nebolo zistene poskodenie") ||
            text.Contains("nenaslo sa ziadne poskodenie") ||
            text.Contains("bez poskodenia") ||
            text.Contains("nebylo zjisteno poskozeni") ||
            text.Contains("zadne poskozeni"))
            return false;

        if (text.Contains("repairable") ||
            text.Contains("poskoden") ||
            text.Contains("opravitel") ||
            text.Contains("corruption") ||
            text.Contains("corrupt"))
            return true;

        // 3010 znamená úspech s požiadavkou na reštart, nie poškodenie.
        return exitCode != 0 && exitCode != 3010;
    }

    internal static bool DetectUnfixed(string output)
    {
        var text = Normalize(output);
        return text.Contains("unable to fix") ||
               text.Contains("could not fix") ||
               text.Contains("could not perform") ||
               text.Contains("nepodarilo sa opravit") ||
               (text.Contains("nezda") && text.Contains("opravit")) ||
               text.Contains("nedokazalo opravit");
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var formD = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) !=
                System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
