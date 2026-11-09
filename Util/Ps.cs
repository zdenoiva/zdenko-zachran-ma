using System.Text.Json;

namespace NtfsRescue.Util;

public static class Ps
{
    private const string Exe = "powershell.exe";

    public static async Task<string?> RunAsync(string script, CancellationToken ct, int timeoutMs = 60000)
    {
        try
        {
            var result = await ProcessRunner.RunAsync(
                Exe,
                new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script },
                null, ct, timeoutMs).ConfigureAwait(false);

            if (result.ExitCode != 0 || result.TimedOut || result.Canceled)
                return null;

            return result.Output.Trim();
        }
        catch
        {
            return null;
        }
    }

    public static async Task<JsonDocument?> RunJsonAsync(string script, CancellationToken ct, int timeoutMs = 60000)
    {
        var text = await RunAsync(script, ct, timeoutMs).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text)) return null;

        // PowerShell obcas prida varovanie pred JSON; najdeme zaciatok.
        var start = text.IndexOfAny(new[] { '{', '[' });
        if (start < 0) return null;
        var json = text[start..].Trim();
        if (json.Length == 0) return null;

        try { return JsonDocument.Parse(json); }
        catch { return null; }
    }

    public static IEnumerable<JsonElement> Objects(JsonDocument doc)
    {
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
                if (item.ValueKind == JsonValueKind.Object) yield return item;
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            yield return root;
        }
    }

    public static string Str(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Array => string.Join(", ", v.EnumerateArray().Select(x => x.ToString())),
            JsonValueKind.Null => "",
            _ => v.ToString()
        };
    }

    public static long Long(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return 0;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l)) return l;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
        return 0;
    }

    public static int Int(JsonElement obj, string name)
    {
        var l = Long(obj, name);
        return l is > int.MaxValue or < int.MinValue ? 0 : (int)l;
    }

    public static bool Bool(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return false;
        return v.ValueKind == JsonValueKind.True;
    }
}
