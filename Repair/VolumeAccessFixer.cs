using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using NtfsRescue.Core;
using NtfsRescue.Util;

namespace NtfsRescue.Repair;

public sealed class AccessFixResult
{
    public bool Mounted;
    public bool Accessible;
    public bool UserAccess;
    public bool PermissionsReset;
    public string Message = "";
    public readonly List<(string Key, string Value)> Rows = new();
}

public static class VolumeAccessFixer
{
    private static string MountVol => Path.Combine(Environment.SystemDirectory, "mountvol.exe");
    private static string PowerShell => "powershell.exe";

    public static async Task<AccessFixResult> FixAsync(
        DiskPartition partition,
        bool resetPermissions,
        Action<string> log,
        IProgress<string>? phase,
        CancellationToken ct)
    {
        var result = new AccessFixResult();
        if (partition.IsImage)
        {
            result.Message = "Obraz disku je len na čítanie; prístup k volume sa nedá obnovovať.";
            return result;
        }
        char? letter = partition.HasLetter ? partition.Letter : null;
        if (letter is null)
        {
            phase?.Report("Priraďujem písmeno");
            letter = await PartitionAccess.AssignDriveLetterAsync(partition, ct).ConfigureAwait(false);
        }

        if (letter is null)
        {
            result.Message = "Nepodarilo sa priradiť písmeno partícii.";
            return result;
        }

        string drive = $"{letter}:";
        phase?.Report("Zisťujem volume GUID");
        var volumeGuid = await GetVolumeGuidAsync(drive, log, ct).ConfigureAwait(false);
        result.Rows.Add(("volume", volumeGuid ?? "neznámy"));

        phase?.Report("Odpájam a pripájam volume");
        // Odstrániť mount point (ak existuje) a pripojiť znova.
        await RunAsync(MountVol, new[] { drive, "/p" }, log, ct).ConfigureAwait(false);
        await Task.Delay(700, ct).ConfigureAwait(false);

        bool added = false;
        if (!string.IsNullOrWhiteSpace(volumeGuid))
        {
            var add = await RunAsync(MountVol, new[] { drive, volumeGuid }, log, ct).ConfigureAwait(false);
            added = add.ExitCode == 0;
        }

        if (!added)
        {
            phase?.Report("Pripájam cez PowerShell");
            added = await AddPartitionAccessPathAsync(partition, drive, log, ct).ConfigureAwait(false);
        }

        await RunAsync(PowerShell,
            new[] { "-NoProfile", "-NonInteractive", "-Command", "Update-HostStorageCache" },
            log, ct, 30000).ConfigureAwait(false);

        await Task.Delay(1200, ct).ConfigureAwait(false);

        var fresh = await VolumeCatalog.GetOneAsync(letter.Value, ct).ConfigureAwait(false);
        result.Mounted = fresh is not null &&
                         !string.IsNullOrWhiteSpace(fresh.FileSystem) &&
                         fresh.Size > 0;
        result.Accessible = Directory.Exists($"{drive}\\");

        result.Rows.Add(("namontovaný", result.Mounted ? "áno" : "nie"));
        result.Rows.Add(("prístup", result.Accessible ? "OK" : "zamietnutý"));

        if (result.Mounted && resetPermissions)
        {
            phase?.Report("Obnovujem oprávnenia");
            result.PermissionsReset = await ResetPermissionsAsync(drive, log, ct).ConfigureAwait(false);
            await Task.Delay(500, ct).ConfigureAwait(false);
            result.Accessible = Directory.Exists($"{drive}\\");
            result.UserAccess = HasUserAccess(letter.Value);
            result.Rows.Add(("oprávnenia", result.PermissionsReset ? "obnovené" : "nepodarilo sa"));
            result.Rows.Add(("prístup po ACL", result.Accessible ? "OK" : "zamietnutý"));
            result.Rows.Add(("ACL pre používateľa", result.UserAccess ? "OK" : "zamietnutý"));
        }
        else
        {
            result.UserAccess = HasUserAccess(letter.Value);
        }

        if (!result.Mounted)
            result.Message = "Volume sa nepodarilo namontovať. Skúste reštart počítača alebo iný USB port.";
        else if (!result.UserAccess)
            result.Message = "Volume je namontovaný, ale ACL nepovoľuje bežnému používateľovi prístup. " +
                             "Skúste obnoviť oprávnenia (ACL).";
        else if (!result.Accessible)
            result.Message = "Volume je namontovaný, ale prístup je zamietnutý.";
        else
            result.Message = "Volume je namontovaný a prístup pre používateľa funguje.";

        log(result.Message);
        return result;
    }

    public static async Task<AccessFixResult> FixAclOnlyAsync(
        DiskPartition partition, Action<string> log, IProgress<string>? phase, CancellationToken ct)
    {
        var result = new AccessFixResult { Mounted = true };
        if (partition.IsImage)
        {
            result.Message = "Obraz disku je len na čítanie.";
            return result;
        }

        char? letter = partition.HasLetter ? partition.Letter : null;
        if (letter is null)
        {
            phase?.Report("Priraďujem písmeno");
            letter = await PartitionAccess.AssignDriveLetterAsync(partition, ct).ConfigureAwait(false);
        }
        if (letter is null)
        {
            result.Message = "Nepodarilo sa priradiť písmeno partícii.";
            return result;
        }

        string drive = $"{letter}:";
        result.Accessible = Directory.Exists($"{drive}\\");
        if (!result.Accessible)
        {
            result.Mounted = false;
            result.Message = "Volume nie je prístupný; najprv treba obnoviť mount.";
            return result;
        }

        phase?.Report("Obnovujem ACL");
        result.PermissionsReset = await ResetPermissionsAsync(drive, log, ct).ConfigureAwait(false);
        await Task.Delay(500, ct).ConfigureAwait(false);
        result.UserAccess = HasUserAccess(letter.Value);
        result.Accessible = Directory.Exists($"{drive}\\");
        result.Rows.Add(("oprávnenia", result.PermissionsReset ? "obnovené" : "nepodarilo sa"));
        result.Rows.Add(("ACL pre používateľa", result.UserAccess ? "OK" : "zamietnutý"));
        result.Message = result.UserAccess
            ? "Oprávnenia na koreni boli obnovené; prístup pre používateľa funguje."
            : "ACL sa nepodarilo obnoviť. Skúste reštart alebo správcu.";
        log(result.Message);
        return result;
    }

    public static bool HasUserAccess(char letter)
    {
        try
        {
            var root = $"{letter}:\\";
            var di = new DriveInfo($"{letter}:");
            if (!di.IsReady) return false;
            if (!di.DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase)) return true;

            var info = new DirectoryInfo(root);
            var acl = info.GetAccessControl();
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var world = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var authenticated = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            var current = WindowsIdentity.GetCurrent().User;

            bool allow = false;
            bool deny = false;

            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.IdentityReference is not SecurityIdentifier sid) continue;
                bool relevant = sid == users || sid == world || sid == authenticated || sid == current;
                if (!relevant) continue;

                if (rule.AccessControlType == AccessControlType.Deny)
                    deny = true;
                else if ((rule.FileSystemRights & FileSystemRights.Read) != 0)
                    allow = true;
            }

            return allow && !deny;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Po chkdsk: ak Windows volume neukazuje ako namontovaný, skúsi remount.</summary>
    public static async Task<bool> RemountAsync(char letter, Action<string> log, CancellationToken ct)
    {
        string drive = $"{letter}:";
        var volumeGuid = await GetVolumeGuidAsync(drive, log, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(volumeGuid)) return false;

        await RunAsync(MountVol, new[] { drive, "/p" }, log, ct).ConfigureAwait(false);
        await Task.Delay(700, ct).ConfigureAwait(false);
        var add = await RunAsync(MountVol, new[] { drive, volumeGuid }, log, ct).ConfigureAwait(false);
        await RunAsync(PowerShell,
            new[] { "-NoProfile", "-NonInteractive", "-Command", "Update-HostStorageCache" },
            log, ct, 30000).ConfigureAwait(false);
        await Task.Delay(1000, ct).ConfigureAwait(false);

        var fresh = await VolumeCatalog.GetOneAsync(letter, ct).ConfigureAwait(false);
        return (add.ExitCode == 0 || fresh is not null) &&
               Directory.Exists($"{drive}\\");
    }

    private static async Task<string?> GetVolumeGuidAsync(string drive, Action<string> log, CancellationToken ct)
    {
        var result = await RunAsync(MountVol, new[] { drive, "/L" }, log, ct).ConfigureAwait(false);
        var match = Regex.Match(result.Output, @"\\\\\?\\Volume\{[0-9a-fA-F\-]+\}\\");
        return match.Success ? match.Value : null;
    }

    private static async Task<bool> AddPartitionAccessPathAsync(
        DiskPartition partition, string drive, Action<string> log, CancellationToken ct)
    {
        if (partition.PartitionNumber <= 0) return false;

        var script =
            $"$d={partition.DiskNumber}; $p={partition.PartitionNumber}; " +
            "try { Remove-PartitionAccessPath -DiskNumber $d -PartitionNumber $p -AccessPath \"" + drive + ":\\\" -ErrorAction SilentlyContinue } catch {}; " +
            "try { Add-PartitionAccessPath -DiskNumber $d -PartitionNumber $p -AccessPath \"" + drive + ":\\\" -ErrorAction Stop; 'OK' } catch { $_.Exception.Message }";

        var result = await ProcessRunner.RunAsync(PowerShell,
            new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script },
            log, ct, 30000).ConfigureAwait(false);
        return result.ExitCode == 0 && result.Output.Contains("OK", StringComparison.Ordinal);
    }

    private static async Task<bool> ResetPermissionsAsync(string drive, Action<string> log, CancellationToken ct)
    {
        string root = drive + "\\";
        log("Resetujem oprávnenia na koreni (takeown + icacls).");

        await RunAsync(Path.Combine(Environment.SystemDirectory, "takeown.exe"),
            new[] { "/F", root, "/A" }, log, ct, 60000).ConfigureAwait(false);

        // Vyčistiť zdedené/poškodené ACL na koreni a nastaviť štandardné práva.
        await RunAsync(Path.Combine(Environment.SystemDirectory, "icacls.exe"),
            new[] { root, "/reset", "/C" }, log, ct, 120000).ConfigureAwait(false);

        await RunAsync(Path.Combine(Environment.SystemDirectory, "icacls.exe"),
            new[] { root, "/inheritance:e", "/C" }, log, ct, 60000).ConfigureAwait(false);

        var admin = await RunAsync(Path.Combine(Environment.SystemDirectory, "icacls.exe"),
            new[] { root, "/grant", "*S-1-5-32-544:(OI)(CI)F", "/C" }, log, ct, 120000).ConfigureAwait(false);

        var users = await RunAsync(Path.Combine(Environment.SystemDirectory, "icacls.exe"),
            new[] { root, "/grant", "*S-1-5-32-545:(OI)(CI)M", "/C" }, log, ct, 120000).ConfigureAwait(false);

        return admin.ExitCode == 0 && users.ExitCode == 0;
    }

    private static async Task<ProcessResult> RunAsync(string exe, string[] args,
        Action<string> log, CancellationToken ct, int timeoutMs = 60000)
    {
        log($"> {Path.GetFileName(exe)} {string.Join(' ', args)}");
        var result = await ProcessRunner.RunAsync(exe, args, line =>
        {
            if (!string.IsNullOrWhiteSpace(line)) log("  " + line.Trim());
        }, ct, timeoutMs).ConfigureAwait(false);
        return result;
    }
}
