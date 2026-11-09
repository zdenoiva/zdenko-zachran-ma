using NtfsRescue.Core;
using NtfsRescue.Util;

namespace NtfsRescue.Repair;

public sealed class RepairOutcome
{
    public int ChkdskExit;
    public bool Repaired;
    public string Summary = "";
    public readonly List<(string Key, string Value)> Rows = new();
}

public sealed class VerifyOutcome
{
    public bool Healthy;
    public bool ChkdskFoundErrors;
    public int ChkdskExit = -1;
    public int TopLevelEntries;
    public string Summary = "";
    public readonly List<(string Key, string Value)> Rows = new();
}

public static class RepairService
{
    public static async Task<RepairOutcome> RepairAsync(
        char letter,
        bool fullSurfaceScan,
        Action<string> log,
        IProgress<string>? phase,
        CancellationToken ct)
    {
        var outcome = new RepairOutcome();

        phase?.Report("chkdsk /f");
        log("");
        log("=== OPRAVA: chkdsk /f" + (fullSurfaceScan ? " /r" : "") + " ===");
        log("Volume bude na čas odpojený. Nezatvárajte aplikáciu.");

        var result = await ChkdskRunner.FixAsync(letter, fullSurfaceScan, log, ct).ConfigureAwait(false);
        outcome.ChkdskExit = result.ExitCode;
        outcome.Rows.Add(("chkdsk exit", $"{result.ExitCode} – {ChkdskRunner.DescribeExitCode(result.ExitCode)}"));
        log("");
        log($"chkdsk ukončený s kódom {result.ExitCode}: {ChkdskRunner.DescribeExitCode(result.ExitCode)}");

        if (result.Canceled)
        {
            outcome.Summary = "Oprava bola zrušená.";
            return outcome;
        }

        // Po chkdsk môže volume ešte chvíľu trvať, kým Windows aktualizuje stav.
        await Task.Delay(1500, ct).ConfigureAwait(false);

        VolumeEntry? fresh = null;
        try { fresh = await VolumeCatalog.GetOneAsync(letter, ct).ConfigureAwait(false); }
        catch { }

        bool needsRemount = fresh is null ||
                            string.IsNullOrWhiteSpace(fresh.FileSystem) ||
                            fresh.Size <= 0 ||
                            !Directory.Exists($"{letter}:\\");
        if (needsRemount)
        {
            log("Volume nie je namontovaný; skúšam ho znova pripojiť...");
            try
            {
                var remounted = await VolumeAccessFixer.RemountAsync(letter, log, ct).ConfigureAwait(false);
                outcome.Rows.Add(("remount", remounted ? "OK" : "nepodarilo sa"));
                await Task.Delay(1000, ct).ConfigureAwait(false);
                fresh = await VolumeCatalog.GetOneAsync(letter, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log("Remount zlyhal: " + ex.Message);
                outcome.Rows.Add(("remount", "chyba: " + ex.Message));
            }
        }

        bool stillProblem = fresh is null || fresh.IsProblem;
        outcome.Rows.Add(("stav po chkdsk", fresh is null ? "neznámy" : $"{fresh.Health} / {fresh.Status}"));

        if (stillProblem)
        {
            log("");
            log("Volume je stále označený ako problematický. Skúšam Repair-Volume -OfflineScanAndFix...");
            try
            {
                var rv = await ChkdskRunner.RepairVolumeAsync(letter, log, ct).ConfigureAwait(false);
                outcome.Rows.Add(("Repair-Volume exit", rv.ExitCode.ToString()));
                await Task.Delay(1500, ct).ConfigureAwait(false);
                fresh = await VolumeCatalog.GetOneAsync(letter, ct).ConfigureAwait(false);
                stillProblem = fresh is null || fresh.IsProblem;
                outcome.Rows.Add(("stav po Repair-Volume", fresh is null ? "neznámy" : $"{fresh.Health} / {fresh.Status}"));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log("Repair-Volume zlyhal: " + ex.Message);
                outcome.Rows.Add(("Repair-Volume", "zlyhal: " + ex.Message));
            }
        }

        if (!Directory.Exists($"{letter}:\\"))
        {
            log("Po Repair-Volume volume nie je namontovaný; skúšam remount...");
            try
            {
                await VolumeAccessFixer.RemountAsync(letter, log, ct).ConfigureAwait(false);
                await Task.Delay(1000, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { log("Remount zlyhal: " + ex.Message); }
        }

        bool readable = CanOpenRoot(letter, log);
        bool userAccess = VolumeAccessFixer.HasUserAccess(letter);

        if (!readable || !userAccess)
        {
            log("");
            log("Prístup nie je v poriadku; automaticky skúšam remount + ACL...");
            var partition = await FindPartitionAsync(letter, ct).ConfigureAwait(false);
            if (partition is not null)
            {
                try
                {
                    var fix = readable && !userAccess
                        ? await VolumeAccessFixer.FixAclOnlyAsync(partition, log, phase, ct).ConfigureAwait(false)
                        : await VolumeAccessFixer.FixAsync(partition, true, log, phase, ct).ConfigureAwait(false);
                    outcome.Rows.Add(("auto obnovenie prístupu", fix.Message));
                    await Task.Delay(1000, ct).ConfigureAwait(false);

                    fresh = await VolumeCatalog.GetOneAsync(letter, ct).ConfigureAwait(false);
                    stillProblem = fresh is null || fresh.IsProblem;
                    readable = CanOpenRoot(letter, log);
                    userAccess = VolumeAccessFixer.HasUserAccess(letter);
                    outcome.Rows.Add(("stav po auto fixe",
                        fresh is null ? "neznámy" : $"{fresh.Health} / {fresh.Status}"));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    log("Automatické obnovenie prístupu zlyhalo: " + ex.Message);
                    outcome.Rows.Add(("auto obnovenie prístupu", "zlyhalo: " + ex.Message));
                }
            }
        }

        outcome.Rows.Add(($"prístup k {letter}:\\", readable ? "OK" : "zamietnutý"));
        outcome.Rows.Add(("ACL pre používateľa", userAccess ? "OK" : "zamietnutý"));
        outcome.Repaired = readable && !stillProblem && userAccess;
        outcome.Summary = outcome.Repaired
            ? "NTFS je opravené a prístup k súborom funguje."
            : !userAccess
                ? "NTFS je opravené, ale ACL stále blokuje bežného používateľa. Skúste reštart počítača."
                : "Oprava sa nedokončila úplne. Pozrite protokol; možno bude potrebný reštart alebo záchrana súborov.";

        log("");
        log("Výsledok: " + outcome.Summary);
        return outcome;
    }

    private static async Task<DiskPartition?> FindPartitionAsync(char letter, CancellationToken ct)
    {
        try
        {
            var parts = await DiskCatalog.LoadAsync(ct).ConfigureAwait(false);
            var found = parts.FirstOrDefault(p =>
                !p.IsImage && p.HasLetter && p.Letter == letter);
            if (found is not null) return found;
        }
        catch { }

        var loc = VolumeMapper.Locate(letter);
        if (!loc.IsValid) return null;

        return new DiskPartition
        {
            DiskNumber = loc.DiskNumber,
            PartitionNumber = 0,
            Offset = loc.Offset,
            Length = loc.Length,
            SectorSize = loc.SectorSize,
            DiskSize = loc.DiskSize,
            DriveLetter = letter.ToString(),
            FileSystem = FileSystemKind.Ntfs,
            Model = loc.Model,
            Serial = loc.Serial
        };
    }

    public static async Task<VerifyOutcome> VerifyAsync(
        char letter,
        Action<string> log,
        IProgress<string>? phase,
        CancellationToken ct)
    {
        var outcome = new VerifyOutcome();

        phase?.Report("Overujem čítaním");
        log("");
        log("=== OVERENIE ===");

        try
        {
            var chk = await ChkdskRunner.ReadOnlyAsync(letter, log, ct).ConfigureAwait(false);
            outcome.ChkdskExit = chk.ExitCode;
            outcome.ChkdskFoundErrors = chk.Output.Contains("Errors found", StringComparison.OrdinalIgnoreCase);
            outcome.Rows.Add(("chkdsk exit", $"{chk.ExitCode} – {ChkdskRunner.DescribeExitCode(chk.ExitCode)}"));
            outcome.Rows.Add(("chkdsk chyby", outcome.ChkdskFoundErrors ? "nájdené" : "žiadne"));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            log("chkdsk zlyhal: " + ex.Message);
            outcome.Rows.Add(("chkdsk", "zlyhal: " + ex.Message));
        }

        VolumeEntry? fresh = null;
        try { fresh = await VolumeCatalog.GetOneAsync(letter, ct).ConfigureAwait(false); }
        catch { }

        if (fresh is not null)
        {
            outcome.Rows.Add(("stav Windows", $"{fresh.Health} / {fresh.Status}"));
            outcome.Rows.Add(("súborový systém", string.IsNullOrWhiteSpace(fresh.FileSystem) ? "nezistený" : fresh.FileSystem));
        }

        outcome.TopLevelEntries = CountTopLevel(letter, log);
        outcome.Rows.Add(("položiek v koreni", outcome.TopLevelEntries.ToString("N0")));

        bool readable = CanOpenRoot(letter, log);
        outcome.Healthy = readable && !outcome.ChkdskFoundErrors && outcome.ChkdskExit is 0 or 1 or 2;
        outcome.Summary = outcome.Healthy
            ? "Volume je čitateľný a chkdsk nehlási neopravené chyby."
            : "Volume ešte nie je úplne v poriadku. Pozrite protokol.";

        log("Výsledok: " + outcome.Summary);
        return outcome;
    }

    public static bool CanOpenRoot(char letter, Action<string>? log = null)
    {
        try
        {
            var root = $"{letter}:\\";
            using var e = Directory.EnumerateFileSystemEntries(root).GetEnumerator();
            e.MoveNext();
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke("Prístup ku koreňu zlyhal: " + ex.Message);
            return false;
        }
    }

    private static int CountTopLevel(char letter, Action<string>? log)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries($"{letter}:\\").Take(1_000_000).Count();
        }
        catch (Exception ex)
        {
            log?.Invoke("Počítanie položiek zlyhalo: " + ex.Message);
            return -1;
        }
    }
}
