using NtfsRescue.Util;

namespace NtfsRescue.Repair;

public static class ChkdskRunner
{
    private static string Exe => Path.Combine(Environment.SystemDirectory, "chkdsk.exe");

    public static Task<ProcessResult> ReadOnlyAsync(char letter, Action<string> onLine, CancellationToken ct)
        => ProcessRunner.RunAsync(Exe, new[] { $"{letter}:" }, onLine, ct, timeoutMs: 0);

    public static Task<ProcessResult> FixAsync(char letter, bool fullSurfaceScan, Action<string> onLine, CancellationToken ct)
    {
        var args = new List<string> { $"{letter}:", "/f", "/x", "/v" };
        if (fullSurfaceScan) args.Add("/r");
        return ProcessRunner.RunAsync(Exe, args, onLine, ct, timeoutMs: 0);
    }

    public static Task<ProcessResult> RepairVolumeAsync(char letter, Action<string> onLine, CancellationToken ct)
    {
        var script =
            $"$r = Repair-Volume -DriveLetter {letter} -OfflineScanAndFix -ErrorAction Stop; " +
            "$r | Out-String";
        return ProcessRunner.RunAsync(
            "powershell.exe",
            new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script },
            onLine, ct, timeoutMs: 0);
    }

    public static string DescribeExitCode(int code) => code switch
    {
        0 => "žiadne chyby",
        1 => "chyby nájdené a opravené",
        2 => "vyčistenie dokončené (potrebný ďalší beh)",
        3 => "chyby sa nepodarilo opraviť",
        4 => "kontrola nebola vykonaná",
        5 => "kontrola bola zrušená",
        _ => $"neznámy stav ({code})"
    };
}
