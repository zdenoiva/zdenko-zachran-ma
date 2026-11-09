using NtfsRescue.Util;

namespace NtfsRescue.Core;

public sealed class VolumeEntry
{
    public char Letter;
    public string Label = "";
    public string FileSystem = "";
    public string Health = "";
    public string Status = "";
    public long Size;
    public long Free;

    public bool HasLetter => Letter is >= 'A' and <= 'Z';

    public bool IsProblem =>
        string.IsNullOrWhiteSpace(FileSystem)
        || FileSystem.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
        || Health.Contains("Warning", StringComparison.OrdinalIgnoreCase)
        || Status.Contains("Repair", StringComparison.OrdinalIgnoreCase)
        || Status.Contains("Failed", StringComparison.OrdinalIgnoreCase);

    public string Display
    {
        get
        {
            var parts = new List<string>();
            parts.Add($"{Letter}:");
            if (!string.IsNullOrWhiteSpace(Label)) parts.Add(Label);
            parts.Add(string.IsNullOrWhiteSpace(FileSystem) ? "nečitateľný" : FileSystem);
            if (Size > 0) parts.Add(Fmt.Bytes(Size));
            var state = !string.IsNullOrWhiteSpace(Status) && Status != "OK" ? Status : Health;
            if (!string.IsNullOrWhiteSpace(state) && state != "Healthy") parts.Add(state);
            return string.Join("  |  ", parts);
        }
    }

    public override string ToString() => Display;
}

public static class VolumeCatalog
{
    public static async Task<List<VolumeEntry>> LoadAsync(CancellationToken ct)
    {
        var list = await LoadByPowerShellAsync(ct).ConfigureAwait(false);
        if (list.Count == 0) list = LoadByDriveInfo();
        list.Sort((a, b) => a.Letter.CompareTo(b.Letter));
        return list;
    }

    private static async Task<List<VolumeEntry>> LoadByPowerShellAsync(CancellationToken ct)
    {
        var script =
            "Get-Volume -ErrorAction SilentlyContinue | Where-Object { $_.DriveLetter -ne $null } | " +
            "ForEach-Object { [pscustomobject]@{ " +
            "DriveLetter = [string]$_.DriveLetter; " +
            "FileSystemLabel = [string]$_.FileSystemLabel; " +
            "FileSystem = [string]$_.FileSystem; " +
            "HealthStatus = [string]$_.HealthStatus; " +
            "OperationalStatus = ($_.OperationalStatus -join ','); " +
            "Size = $_.Size; SizeRemaining = $_.SizeRemaining } } | " +
            "ConvertTo-Json -Compress";

        var doc = await Ps.RunJsonAsync(script, ct, 30000).ConfigureAwait(false);
        var result = new List<VolumeEntry>();
        if (doc is null) return result;

        foreach (var obj in Ps.Objects(doc))
        {
            var letterText = Ps.Str(obj, "DriveLetter").Trim();
            if (string.IsNullOrWhiteSpace(letterText)) continue;
            char letter;
            if (letterText.Length == 1)
                letter = char.ToUpperInvariant(letterText[0]);
            else if (int.TryParse(letterText, out int letterCode) && letterCode is >= 65 and <= 90)
                letter = (char)letterCode;
            else
                continue;
            if (letter is < 'A' or > 'Z') continue;

            var e = new VolumeEntry
            {
                Letter = letter,
                Label = Ps.Str(obj, "FileSystemLabel"),
                FileSystem = Ps.Str(obj, "FileSystem"),
                Health = Ps.Str(obj, "HealthStatus"),
                Status = Ps.Str(obj, "OperationalStatus"),
                Size = Ps.Long(obj, "Size"),
                Free = Ps.Long(obj, "SizeRemaining")
            };
            if (e.Size <= 0)
            {
                try
                {
                    var di = new DriveInfo($"{letter}:");
                    if (di.IsReady) { e.Size = di.TotalSize; e.Free = di.AvailableFreeSpace; }
                }
                catch { }
            }
            result.Add(e);
        }

        return result;
    }

    private static List<VolumeEntry> LoadByDriveInfo()
    {
        var result = new List<VolumeEntry>();
        foreach (var di in DriveInfo.GetDrives())
        {
            try
            {
                if (di.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                if (string.IsNullOrEmpty(di.Name) || di.Name.Length < 2) continue;
                var letter = char.ToUpperInvariant(di.Name[0]);
                if (letter is < 'A' or > 'Z') continue;

                var e = new VolumeEntry { Letter = letter };
                try
                {
                    if (di.IsReady)
                    {
                        e.Label = di.VolumeLabel;
                        e.FileSystem = di.DriveFormat;
                        e.Size = di.TotalSize;
                        e.Free = di.AvailableFreeSpace;
                    }
                }
                catch { }
                result.Add(e);
            }
            catch { }
        }
        return result;
    }

    public static async Task<VolumeEntry?> GetOneAsync(char letter, CancellationToken ct)
    {
        var all = await LoadAsync(ct).ConfigureAwait(false);
        return all.FirstOrDefault(v => v.Letter == char.ToUpperInvariant(letter));
    }
}
