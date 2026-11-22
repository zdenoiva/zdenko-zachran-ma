using System.Globalization;
using System.Text;
using NtfsRescue.Core;
using NtfsRescue.Repair;
using NtfsRescue.Util;

namespace NtfsRescue.Ui;

public enum SurfaceJobState
{
    Pending,
    Running,
    Stopped,
    Done,
    Failed
}

/// <summary>Jeden test povrchu: vlastný disk, rozsah, mapa, zámok a priebeh.</summary>
public sealed class SurfaceJob
{
    public const int TargetMapCells = 2048;

    public readonly DiskInfo Disk;
    public readonly int FirstBlock;
    public readonly int LastBlock;
    public readonly int[] Cells;
    public readonly double[] CellMs;
    public readonly HashSet<long> BadSectors = new();
    public readonly long CellBytes;
    public readonly int BlocksPerCell;
    public readonly int TotalBlocks;
    public readonly long TotalBytes;

    private readonly object _sync = new();
    private readonly List<string> _log = new();
    private readonly DiskLock _initialLock;
    private CancellationTokenSource? _cts;
    private Task _task = Task.CompletedTask;

    public SurfaceJobState State { get; private set; } = SurfaceJobState.Pending;
    public int Percent { get; private set; }
    public double BytesPerSecond { get; private set; }
    public TimeSpan Elapsed { get; private set; }
    public TimeSpan Eta { get; private set; }
    public long Position { get; private set; }
    public int BlocksDone { get; private set; }
    public string Status { get; private set; } = "Pripravené";
    public string? Error { get; private set; }
    public int LogVersion { get; private set; }

    public SurfaceJob(DiskInfo disk, int firstBlock, int lastBlock, DiskLock diskLock)
    {
        Disk = disk;
        FirstBlock = firstBlock;
        LastBlock = lastBlock;
        _initialLock = diskLock;

        int fullBlocks = SurfaceScanner.BlockCount(disk.Size);
        BlocksPerCell = Math.Max(1, (int)Math.Ceiling((double)fullBlocks / TargetMapCells));
        int cellCount = Math.Max(1, (int)Math.Ceiling((double)fullBlocks / BlocksPerCell));
        CellBytes = (long)BlocksPerCell * SurfaceScanner.BlockSize;
        Cells = new int[cellCount];
        CellMs = new double[cellCount];

        TotalBlocks = lastBlock - firstBlock + 1;
        long start = (long)firstBlock * SurfaceScanner.BlockSize;
        long end = Math.Min(disk.Size, (long)(lastBlock + 1) * SurfaceScanner.BlockSize);
        TotalBytes = Math.Max(0, end - start);
    }

    public bool IsActive => State is SurfaceJobState.Pending or SurfaceJobState.Running;

    public bool HasData
    {
        get
        {
            lock (_sync)
            {
                foreach (var state in Cells)
                    if (state > 0) return true;
                return BadSectors.Count > 0;
            }
        }
    }

    public string[] GetLog()
    {
        lock (_sync) return _log.ToArray();
    }

    public SurfaceSnapshot Snapshot()
    {
        lock (_sync)
        {
            int badCells = 0;
            int knownCells = 0;
            foreach (var state in Cells)
            {
                if (state > 0) knownCells++;
                if (state >= 5) badCells++;
            }
            return new SurfaceSnapshot(State, Percent, BytesPerSecond, Elapsed, Eta,
                Position, BlocksDone, TotalBlocks, BadSectors.Count, badCells, knownCells,
                Status, Error, LogVersion);
        }
    }

    public void Start()
    {
        if (State == SurfaceJobState.Running) return;
        State = SurfaceJobState.Running;
        Status = "Spúšťam kontrolu povrchu…";
        AppendLog($"Disk {Disk.Number} je zamknutý pre kontrolu povrchu.");
        AppState.SurfaceBusy(true);
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _task = Task.Run(() => RunAsync(token), token);
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        if (State == SurfaceJobState.Pending)
        {
            State = SurfaceJobState.Stopped;
            Status = "Zastavené";
        }
    }

    public async Task WaitAsync(int timeoutMs)
    {
        try { await Task.WhenAny(_task, Task.Delay(timeoutMs)).ConfigureAwait(false); }
        catch { }
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            var result = await Task.Run(() => SurfaceScanner.Scan(Disk, FirstBlock, LastBlock,
                new InlineProgress(UpdateProgress), AppendLog, token, OnBlock, OnBadSector),
                token).ConfigureAwait(false);

            lock (_sync)
            {
                foreach (var sector in result.BadSectors) BadSectors.Add(sector);
                Percent = 100;
                BlocksDone = TotalBlocks;
                Position = Math.Min(Disk.Size, (long)(result.LastBlock + 1) * SurfaceScanner.BlockSize);
                Elapsed = result.Duration;
                Eta = TimeSpan.Zero;
                BytesPerSecond = result.Duration.TotalSeconds > 0
                    ? result.TotalBytes / result.Duration.TotalSeconds
                    : 0;
                Status = result.Summary;
                State = SurfaceJobState.Done;
            }
            AppendLog(result.Summary);
            Log.Info(result.Summary);
        }
        catch (OperationCanceledException)
        {
            lock (_sync)
            {
                State = SurfaceJobState.Stopped;
                Status = "Kontrola bola zrušená.";
            }
            AppendLog("Kontrola bola zrušená. Namerené výsledky zostávajú v mape.");
        }
        catch (Exception ex)
        {
            lock (_sync)
            {
                State = SurfaceJobState.Failed;
                Status = "Kontrola zlyhala.";
                Error = ex.Message;
            }
            AppendLog("Kontrola zlyhala: " + ex.Message);
            Log.Warn("Kontrola povrchu zlyhala: " + ex.Message);
        }
        finally
        {
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            _initialLock.Dispose();
            AppState.SurfaceBusy(false);
        }
    }

    private sealed class InlineProgress : IProgress<SurfaceProgress>
    {
        private readonly Action<SurfaceProgress> _action;
        public InlineProgress(Action<SurfaceProgress> action) => _action = action;
        public void Report(SurfaceProgress value) => _action(value);
    }

    private void UpdateProgress(SurfaceProgress p)
    {
        lock (_sync)
        {
            Percent = p.TotalBytes > 0 ? (int)(p.DoneBytes * 100 / p.TotalBytes) : 0;
            BlocksDone = p.BlocksDone;
            BytesPerSecond = p.BytesPerSecond;
            Elapsed = p.Elapsed;
            Eta = p.Eta;
            Position = p.Position;
            Status = $"Kontrola · {p.BlocksDone:N0} / {p.TotalBlocks:N0} blokov";
        }
    }

    private void OnBlock(int index, SurfaceBlock block)
    {
        int cell = index / BlocksPerCell;
        int severity = SurfaceScanner.Severity(block);
        lock (_sync)
        {
            if (cell < 0 || cell >= Cells.Length) return;
            if (severity > Cells[cell]) Cells[cell] = severity;
            if (block.Ms > CellMs[cell]) CellMs[cell] = block.Ms;
        }
    }

    private void OnBadSector(long offset)
    {
        lock (_sync) BadSectors.Add(offset);
    }

    private void AppendLog(string line)
    {
        lock (_sync)
        {
            _log.Add(line);
            if (_log.Count > 500) _log.RemoveAt(0);
            LogVersion++;
        }
        Log.Write("Povrch: " + line);
    }

    public async Task RepairAsync(IWin32Window? owner)
    {
        List<long> sectors;
        lock (_sync) sectors = BadSectors.OrderBy(x => x).ToList();
        if (sectors.Count == 0) return;

        var text =
            $"Disk {Disk.Number} · {Disk.Model} · {Fmt.Bytes(Disk.Size)}\n\n" +
            $"Vadných sektorov: {sectors.Count:N0}\n\n" +
            "Program sa pokúsi znovu prečítať každý vadný sektor, uloží čitateľné dáta " +
            "do záložného súboru a zapíše ich späť. Ak sa sektor nedá prečítať, zapíše nuly " +
            "a pokúsi sa vynútiť preradenie sektora diskom.\n\n" +
            "POZOR: Dáta v poškodených sektoroch môžu byť nenávratne stratené. " +
            "Pri SSD nemusí zápis vynútiť fyzické preradenie.\n\nPokračovať?";

        using var confirm = new ConfirmForm("Oprava vadných sektorov", text, "OPRAVIŤ",
            "Rozumiem, že oprava zapisuje do disku a môže poškodiť dáta.");
        if (confirm.ShowDialog(owner) != DialogResult.OK) return;

        var diskLock = DiskLock.TryAcquire(Disk.Number, "oprava povrchu", out string lockOwner);
        if (!diskLock.Acquired)
        {
            MessageBox.Show(
                $"Disk {Disk.Number} sa práve používa v inom okne programu" +
                (string.IsNullOrWhiteSpace(lockOwner) ? "" : $" ({lockOwner})") +
                ".\n\nPočkajte na dokončenie alebo zavrite druhé okno.",
                "Oprava vadných sektorov", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        State = SurfaceJobState.Running;
        Status = "Opravujem vadné sektory…";
        AppState.SurfaceBusy(true);
        SurfaceRepairResult? result = null;
        Exception? failure = null;
        _cts = new CancellationTokenSource();
        _task = Task.Run(async () =>
        {
            try
            {
                var progress = new InlineRepairProgress(p =>
                {
                    lock (_sync)
                    {
                        Percent = p.Total > 0 ? Math.Clamp(p.Attempted * 100 / p.Total, 0, 100) : 0;
                        Status = $"{p.Phase}: {p.Attempted:N0}/{p.Total:N0} · " +
                                 $"opravené {p.Repaired:N0} · chybné {p.Failed:N0}";
                    }
                });

                result = await SurfaceRepairService.RepairAsync(Disk, sectors, progress,
                    AppendLog, _cts.Token).ConfigureAwait(false);

                lock (_sync)
                {
                    State = SurfaceJobState.Done;
                    Status = result.Summary;
                    Percent = 100;
                }
                AppendLog(result.Summary);
            }
            catch (OperationCanceledException)
            {
                lock (_sync)
                {
                    State = SurfaceJobState.Stopped;
                    Status = "Oprava bola zrušená.";
                }
                AppendLog("Oprava bola zrušená.");
            }
            catch (Exception ex)
            {
                failure = ex;
                lock (_sync)
                {
                    State = SurfaceJobState.Failed;
                    Status = "Oprava zlyhala.";
                    Error = ex.Message;
                }
                AppendLog("Oprava zlyhala: " + ex.Message);
            }
            finally
            {
                try { _cts?.Dispose(); } catch { }
                _cts = null;
                diskLock.Dispose();
                AppState.SurfaceBusy(false);
            }
        });
        await _task;

        if (owner is null) return;
        if (failure is not null)
        {
            MessageBox.Show(owner, "Oprava zlyhala:\n\n" + failure.Message,
                "Oprava vadných sektorov", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        else if (result is not null)
        {
            MessageBox.Show(owner,
                result.Summary + (string.IsNullOrWhiteSpace(result.BackupPath)
                    ? "" : "\n\nZáloha čitateľných dát:\n" + result.BackupPath) +
                "\n\nSpustite kontrolu povrchu znova a overte stav.",
                "Oprava vadných sektorov", MessageBoxButtons.OK,
                result.Failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }

    private sealed class InlineRepairProgress : IProgress<SurfaceRepairProgress>
    {
        private readonly Action<SurfaceRepairProgress> _action;
        public InlineRepairProgress(Action<SurfaceRepairProgress> action) => _action = action;
        public void Report(SurfaceRepairProgress value) => _action(value);
    }

    public void SaveReport(IWin32Window? owner)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV report|*.csv|Všetky súbory|*.*",
            FileName = $"povrch-disk-{Disk.Number}-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            Title = "Uložiť report kontroly povrchu"
        };
        if (dialog.ShowDialog(owner) != DialogResult.OK) return;

        int[] cells;
        double[] ms;
        List<long> sectors;
        lock (_sync)
        {
            cells = (int[])Cells.Clone();
            ms = (double[])CellMs.Clone();
            sectors = BadSectors.OrderBy(x => x).ToList();
        }

        var sb = new StringBuilder();
        sb.AppendLine("Zdenko, zachran ma! - kontrola povrchu");
        sb.AppendLine($"disk;{Disk.Number};{Disk.Model};{Disk.Size};{CellBytes}");
        sb.AppendLine("offset,size,ms,status");
        for (int i = 0; i < cells.Length; i++)
        {
            int state = cells[i];
            if (state <= 0) continue;
            var status = state switch
            {
                5 => "CHYBA",
                4 => "250+ ms",
                3 => "100+ ms",
                2 => "50+ ms",
                _ => "OK"
            };
            long offset = i * CellBytes;
            sb.AppendLine($"{offset},{CellBytes}," +
                          $"{ms[i].ToString("F1", CultureInfo.InvariantCulture)},{status}");
        }
        sb.AppendLine();
        sb.AppendLine("# vadne sektory");
        foreach (var offset in sectors)
            sb.AppendLine(offset.ToString(CultureInfo.InvariantCulture));

        File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(false));
        AppendLog("Report uložený: " + dialog.FileName);
        MessageBox.Show(owner, "Report bol uložený:\n" + dialog.FileName,
            "Kontrola povrchu", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}

public readonly record struct SurfaceSnapshot(
    SurfaceJobState State,
    int Percent,
    double BytesPerSecond,
    TimeSpan Elapsed,
    TimeSpan Eta,
    long Position,
    int BlocksDone,
    int TotalBlocks,
    int BadSectors,
    int BadCells,
    int KnownCells,
    string Status,
    string? Error,
    int LogVersion);
