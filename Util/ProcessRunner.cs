using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace NtfsRescue.Util;

public sealed record ProcessResult(int ExitCode, string Output, bool TimedOut = false, bool Canceled = false)
{
    public bool Success => ExitCode == 0;
}

public static class ProcessRunner
{
    public static Encoding ConsoleEncoding
    {
        get
        {
            try
            {
                var cp = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
                return Encoding.GetEncoding(cp);
            }
            catch
            {
                return Encoding.UTF8;
            }
        }
    }

    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        Action<string>? onLine,
        CancellationToken ct,
        int timeoutMs = 0,
        string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = ConsoleEncoding,
            StandardErrorEncoding = ConsoleEncoding,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        var output = new StringBuilder();
        var outputLock = new object();

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Nepodarilo sa spustiť {fileName}.");
        }
        catch (Exception ex)
        {
            throw new IOException($"Nepodarilo sa spustiť {fileName}: {ex.Message}", ex);
        }

        async Task PumpAsync(StreamReader reader)
        {
            while (true)
            {
                string? line;
                try { line = await reader.ReadLineAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (IOException) { break; }

                if (line is null) break;

                lock (outputLock) output.AppendLine(line);
                try { onLine?.Invoke(line); } catch { }
            }
        }

        var outTask = PumpAsync(process.StandardOutput);
        var errTask = PumpAsync(process.StandardError);

        using var timeoutCts = timeoutMs > 0
            ? new CancellationTokenSource(timeoutMs)
            : new CancellationTokenSource();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        var killed = false;
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            killed = true;
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        }

        try { await Task.WhenAll(outTask, errTask).ConfigureAwait(false); } catch { }

        var text = output.ToString();
        return new ProcessResult(
            process.HasExited ? process.ExitCode : -1,
            text,
            TimedOut: timeoutMs > 0 && timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested,
            Canceled: ct.IsCancellationRequested || killed);
    }

    public static Task<ProcessResult> RunAsync(string fileName, string arguments, Action<string>? onLine,
        CancellationToken ct, int timeoutMs = 0, string? workingDirectory = null)
        => RunAsync(fileName, SplitArguments(arguments), onLine, ct, timeoutMs, workingDirectory);

    private static IEnumerable<string> SplitArguments(string args)
    {
        // Jednoduchy rozdelovac pre pripady, ked potrebujeme jeden argument s medzerami.
        // Pre zlozite pripady pouzivame ArgumentList priamo.
        var list = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        foreach (var c in args)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (sb.Length > 0) { list.Add(sb.ToString()); sb.Clear(); }
            }
            else sb.Append(c);
        }
        if (sb.Length > 0) list.Add(sb.ToString());
        return list;
    }
}
