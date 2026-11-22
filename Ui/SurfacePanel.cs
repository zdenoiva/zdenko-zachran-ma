using NtfsRescue.Core;
using NtfsRescue.Repair;
using NtfsRescue.Util;

namespace NtfsRescue.Ui;

public sealed class SurfacePanel : UserControl
{
    private readonly ComboBox _diskCombo = new();
    private readonly NumericUpDown _startBlock = new();
    private readonly NumericUpDown _endBlock = new();
    private readonly Button _btnReload = new();
    private readonly Button _btnScan = new();
    private readonly Button _btnStopAll = new();
    private readonly Button _btnClear = new();
    private readonly Label _info = new();
    private readonly Label _blocked = new();
    private readonly Label _warning = new();
    private readonly Label _rangeHint = new();
    private readonly FlowLayoutPanel _jobsHost = new();
    private readonly Label _empty = new();
    private readonly System.Windows.Forms.Timer _timer = new();

    private readonly List<SurfaceJob> _jobs = new();
    private readonly List<SurfaceJobCard> _cards = new();
    private SurfaceJobCard? _expanded;
    private List<DiskInfo> _disks = new();
    private DiskInfo? _selected;
    private bool _loaded;
    private bool _loading;

    public SurfacePanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        BuildUi();

        _timer.Interval = 200;
        _timer.Tick += (_, _) =>
        {
            foreach (var card in _cards) card.RefreshFromJob();
            _empty.Visible = _cards.Count == 0;
            _btnStopAll.Enabled = _jobs.Any(j => j.IsActive);
        };
        _timer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }

    public async Task EnsureLoadedAsync()
    {
        if (!_loaded) await LoadDisksAsync();
    }

    public bool SelectDiskNumber(int number)
    {
        for (int i = 0; i < _diskCombo.Items.Count; i++)
        {
            if (_diskCombo.Items[i] is not DiskInfo disk || disk.Number != number) continue;
            _diskCombo.SelectedIndex = i;
            var job = _jobs.FirstOrDefault(j => j.Disk.Number == number);
            if (job is not null) FocusJob(job);
            return true;
        }
        return false;
    }

    public async Task ReloadAsync()
    {
        _loaded = false;
        await LoadDisksAsync();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 14, 18, 14),
            BackColor = Theme.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildToolbar(), 0, 0);
        root.Controls.Add(BuildInfo(), 0, 1);
        root.Controls.Add(BuildJobs(), 0, 2);
        Controls.Add(root);
    }

    private Control BuildToolbar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 2,
            BackColor = Theme.Background,
            Margin = new Padding(0, 0, 0, 10)
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var diskRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Theme.Background,
            Margin = new Padding(0, 0, 8, 0)
        };
        diskRow.Controls.Add(KeyLabel("Disk:"));
        _diskCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _diskCombo.Width = 520;
        _diskCombo.Margin = new Padding(0, 4, 0, 0);
        _diskCombo.SelectedIndexChanged += (_, _) => SelectDisk();
        diskRow.Controls.Add(_diskCombo);
        bar.Controls.Add(diskRow, 0, 0);

        var topButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            BackColor = Theme.Background,
            Margin = new Padding(0)
        };
        _btnReload.Text = "Obnoviť disky";
        StyleSecondary(_btnReload);
        _btnReload.Click += async (_, _) => await LoadDisksAsync();
        topButtons.Controls.Add(_btnReload);

        _btnStopAll.Text = "Zastaviť všetko";
        StyleSecondary(_btnStopAll);
        _btnStopAll.Enabled = false;
        _btnStopAll.Click += (_, _) => StopAll();
        topButtons.Controls.Add(_btnStopAll);

        _btnClear.Text = "Vyčistiť dokončené";
        StyleSecondary(_btnClear);
        _btnClear.Click += (_, _) => ClearCompleted();
        topButtons.Controls.Add(_btnClear);

        _btnScan.Text = "Spustiť test";
        StylePrimary(_btnScan);
        _btnScan.Click += async (_, _) => await StartTestAsync();
        topButtons.Controls.Add(_btnScan);
        bar.Controls.Add(topButtons, 1, 0);

        var rangeRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Theme.Background,
            Margin = new Padding(0, 8, 8, 0)
        };
        rangeRow.Controls.Add(KeyLabel("Od bloku:"));
        StyleNumeric(_startBlock);
        rangeRow.Controls.Add(_startBlock);
        rangeRow.Controls.Add(KeyLabel("Do bloku:", 12));
        StyleNumeric(_endBlock);
        rangeRow.Controls.Add(_endBlock);
        _rangeHint.AutoSize = true;
        _rangeHint.ForeColor = Theme.Muted;
        _rangeHint.Font = Theme.Small;
        _rangeHint.Margin = new Padding(6, 9, 0, 0);
        rangeRow.Controls.Add(_rangeHint);
        bar.Controls.Add(rangeRow, 0, 1);
        bar.SetColumnSpan(rangeRow, 2);

        return bar;
    }

    private Control BuildInfo()
    {
        var card = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = Theme.Surface,
            Padding = new Padding(16),
            Margin = new Padding(0, 0, 0, 10)
        };
        PaintBorder(card);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Surface
        };
        _info.Text = "Vyberte disk.";
        _info.AutoSize = true;
        _info.MaximumSize = new Size(1100, 0);
        _info.Font = Theme.Body;
        _info.ForeColor = Theme.Text;
        layout.Controls.Add(_info);

        _blocked.AutoSize = true;
        _blocked.MaximumSize = new Size(1100, 0);
        _blocked.ForeColor = Theme.Danger;
        _blocked.Font = new Font("Segoe UI Semibold", 9f);
        _blocked.Visible = false;
        _blocked.Margin = new Padding(0, 6, 0, 0);
        layout.Controls.Add(_blocked);

        _warning.AutoSize = true;
        _warning.MaximumSize = new Size(1100, 0);
        _warning.ForeColor = Theme.Warning;
        _warning.Font = Theme.Small;
        _warning.Margin = new Padding(0, 6, 0, 0);
        _warning.Text =
            "Kontrola povrchu je len čítanie a môže trvať desiatky minút až hodiny. " +
            "Testy môžu bežať súčasne; na jednom USB mostíku sa delia o rýchlosť. " +
            "Oprava zapisuje do vadných sektorov — dáta v nich môžu byť nenávratne zničené.";
        layout.Controls.Add(_warning);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildJobs()
    {
        _jobsHost.Dock = DockStyle.Fill;
        _jobsHost.AutoScroll = true;
        _jobsHost.FlowDirection = FlowDirection.TopDown;
        _jobsHost.WrapContents = false;
        _jobsHost.BackColor = Theme.Background;
        _jobsHost.Padding = new Padding(0);
        _jobsHost.Resize += (_, _) => ResizeCards();

        _empty.Text = "Zatiaľ nemáte spustený žiadny test. Vyberte disk a spustite kontrolu.";
        _empty.AutoSize = false;
        _empty.Height = 30;
        _empty.Width = 600;
        _empty.Font = Theme.Body;
        _empty.ForeColor = Theme.Muted;
        _empty.TextAlign = ContentAlignment.MiddleLeft;
        _empty.Margin = new Padding(2, 8, 0, 0);
        _jobsHost.Controls.Add(_empty);

        return _jobsHost;
    }

    private static Label KeyLabel(string text, int leftMargin = 0) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI Semibold", 9.5f),
        ForeColor = Theme.Text,
        Margin = new Padding(leftMargin, 8, 8, 0)
    };

    private static void StyleSecondary(Button button)
    {
        button.AutoSize = true;
        button.Height = 34;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = Theme.Surface;
        button.ForeColor = Theme.Text;
        button.Font = new Font("Segoe UI Semibold", 9f);
        button.Cursor = Cursors.Hand;
        button.Padding = new Padding(12, 0, 12, 0);
        button.Margin = new Padding(0, 2, 8, 0);
        button.FlatAppearance.BorderColor = Theme.Border;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.MouseOverBackColor = Theme.SurfaceAlt;
    }

    private static void StylePrimary(Button button)
    {
        StyleSecondary(button);
        button.BackColor = Theme.Accent;
        button.ForeColor = Color.White;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Theme.AccentDark;
    }

    private static void StyleNumeric(NumericUpDown numeric)
    {
        numeric.Width = 130;
        numeric.Minimum = 0;
        numeric.Maximum = 0;
        numeric.ThousandsSeparator = true;
        numeric.Font = Theme.Body;
        numeric.Margin = new Padding(0, 5, 0, 0);
    }

    private static void PaintBorder(Panel panel)
    {
        panel.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
        };
    }

    private async Task LoadDisksAsync()
    {
        if (_loading) return;
        _loading = true;
        _btnReload.Enabled = false;
        _diskCombo.Enabled = false;
        _btnScan.Enabled = false;
        try
        {
            var previousNumber = _selected?.Number;
            _disks = await DiskInfoCatalog.LoadAsync(CancellationToken.None);
            _diskCombo.Items.Clear();
            foreach (var disk in _disks) _diskCombo.Items.Add(disk);
            if (_disks.Count == 0)
            {
                _selected = null;
                _info.Text = "Nenašli sa žiadne fyzické disky.";
                return;
            }

            _selected = previousNumber.HasValue
                ? _disks.FirstOrDefault(d => d.Number == previousNumber.Value) ?? _disks[0]
                : _disks.FirstOrDefault(d => !d.IsCurrentSystem) ?? _disks[0];
            _diskCombo.SelectedItem = _selected;
            SelectDisk();
            _loaded = true;
        }
        catch (Exception ex)
        {
            _info.Text = "Disky sa nepodarilo načítať: " + ex.Message;
            _info.ForeColor = Theme.Danger;
        }
        finally
        {
            _loading = false;
            _btnReload.Enabled = true;
            _diskCombo.Enabled = true;
            _btnScan.Enabled = _selected is not null;
        }
    }

    private void SelectDisk()
    {
        _selected = _diskCombo.SelectedItem as DiskInfo;
        if (_selected is null)
        {
            _info.Text = "Vyberte disk.";
            _blocked.Visible = false;
            _btnScan.Enabled = false;
            _startBlock.Maximum = 0;
            _endBlock.Maximum = 0;
            _rangeHint.Text = "";
            return;
        }

        var d = _selected;
        _info.ForeColor = Theme.Text;
        _info.Text =
            $"Disk {d.Number} · {d.Model} · {Fmt.Bytes(d.Size)} · " +
            $"{(string.IsNullOrWhiteSpace(d.PartitionStyle) ? "?" : d.PartitionStyle)} · {d.BusType}" +
            (string.IsNullOrWhiteSpace(Fmt.Serial(d.Serial)) ? "" : " · S/N " + Fmt.Serial(d.Serial));

        var appDisk = VolumeMapper.GetDiskNumberForPath(Environment.ProcessPath ?? "");
        string blocked = "";
        if (d.IsCurrentSystem)
            blocked = "Aktuálny systémový disk: kontrola povrchu je povolená len na čítanie; oprava zápisom je zablokovaná.";
        else if (appDisk >= 0 && appDisk == d.Number)
            blocked = "Program beží z tohto disku. Oprava zápisom je zablokovaná.";
        _blocked.Text = blocked;
        _blocked.Visible = blocked.Length > 0;

        int fullBlocks = SurfaceScanner.BlockCount(d.Size);
        _startBlock.Maximum = Math.Max(0, fullBlocks - 1);
        _endBlock.Maximum = Math.Max(0, fullBlocks - 1);
        _startBlock.Value = 0;
        _endBlock.Value = _endBlock.Maximum;
        _rangeHint.Text = $"z {fullBlocks:N0} blokov · blok = {Fmt.Bytes(SurfaceScanner.BlockSize)}";
        _btnScan.Enabled = !_loading;
    }

    private async Task StartTestAsync()
    {
        if (_selected is null) return;
        var disk = _selected;
        if (disk.Size <= 0)
        {
            MessageBox.Show("Neznáma veľkosť disku.", "Kontrola povrchu",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int first = (int)_startBlock.Value;
        int last = (int)_endBlock.Value;
        if (last < first)
        {
            MessageBox.Show("Koncový blok musí byť rovnaký alebo väčší ako začiatočný.",
                "Kontrola povrchu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var existing = _jobs.FirstOrDefault(j => j.Disk.Number == disk.Number);
        if (existing is not null)
        {
            if (existing.IsActive)
            {
                FocusJob(existing);
                MessageBox.Show(
                    $"Disk {disk.Number} už testujem. Zobrazil som jeho kartu.",
                    "Kontrola povrchu", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var replace = MessageBox.Show(
                $"Pre disk {disk.Number} už existuje dokončený test.\n\n" +
                "Nahradiť ho novým testom?",
                "Kontrola povrchu", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (replace != DialogResult.Yes)
            {
                FocusJob(existing);
                return;
            }
            RemoveJob(existing);
        }

        var diskLock = DiskLock.TryAcquire(disk.Number, "kontrola povrchu", out string owner);
        if (!diskLock.Acquired)
        {
            MessageBox.Show(
                $"Disk {disk.Number} sa práve používa v inom okne programu" +
                (string.IsNullOrWhiteSpace(owner) ? "" : $" ({owner})") +
                ".\n\nPočkajte na dokončenie alebo zavrite druhé okno.",
                "Kontrola povrchu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (disk.BusType.Contains("USB", StringComparison.OrdinalIgnoreCase) &&
            _jobs.Any(j => j.IsActive &&
                           j.Disk.BusType.Contains("USB", StringComparison.OrdinalIgnoreCase)))
        {
            var run = MessageBox.Show(
                "Na USB už beží iný test.\n\n" +
                "Súbežné testy na USB mostíku sa delia o rýchlosť — môže byť približne " +
                "polovičná.\n\nSpustiť aj tak?",
                "Kontrola povrchu", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (run != DialogResult.Yes)
            {
                diskLock.Dispose();
                return;
            }
        }

        // Ak box po spánku hlási "not ready", skúsime ho prebudiť/resetovať
        // ešte pred vytvorením testu, aby sa hneď neukončil.
        var wake = AtaSmart.WakeUpDisk(disk.Number);
        if (wake.StartsWith("read 21", StringComparison.Ordinal))
        {
            if (!_jobs.Any(j => j.IsActive) && UsbBridge.AutoReset &&
                UsbBridge.Reset(disk.Number))
            {
                await Task.Delay(5000);
                wake = AtaSmart.WakeUpDisk(disk.Number);
            }
            if (wake.StartsWith("read 21", StringComparison.Ordinal))
            {
                diskLock.Dispose();
                MessageBox.Show(
                    "Disk alebo USB box nie je pripravený (chyba 21).\n\n" +
                    "Odpojte a znova pripojte USB kábel a napájanie boxu, prípadne " +
                    "reštartujte počítač. Potom spustite test znova.",
                    "Kontrola povrchu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        var job = new SurfaceJob(disk, first, last, diskLock);
        var card = new SurfaceJobCard(job)
        {
            Width = CardWidth()
        };
        card.DetailRequested += ToggleDetail;
        card.StopRequested += c => c.Job.Stop();
        card.RemoveRequested += async c => await RemoveJobAsync(c);
        card.RepairRequested += async c => await RepairJobAsync(c);
        card.ReportRequested += c => c.Job.SaveReport(FindForm());

        _jobs.Add(job);
        _cards.Add(card);
        _jobsHost.Controls.Add(card);
        _empty.Visible = false;
        ResizeCards();
        job.Start();

        // Prvý test otvoríme v detaile ako doteraz; druhý prepne na spoločný prehľad.
        if (_cards.Count == 1)
        {
            _expanded = card;
            card.SetExpanded(true);
        }
        else if (_expanded is not null)
        {
            _expanded.SetExpanded(false);
            _expanded = null;
        }

        _jobsHost.ScrollControlIntoView(card);
        await Task.CompletedTask;
    }

    private void ToggleDetail(SurfaceJobCard card)
    {
        if (_expanded == card)
        {
            card.SetExpanded(false);
            _expanded = null;
            return;
        }

        _expanded?.SetExpanded(false);
        _expanded = card;
        card.SetExpanded(true);
        _jobsHost.ScrollControlIntoView(card);
    }

    private void FocusJob(SurfaceJob job)
    {
        var card = _cards.FirstOrDefault(c => c.Job == job);
        if (card is null) return;
        if (_expanded != card)
        {
            _expanded?.SetExpanded(false);
            _expanded = card;
            card.SetExpanded(true);
        }
        _jobsHost.ScrollControlIntoView(card);
    }

    private async Task RemoveJobAsync(SurfaceJobCard card)
    {
        var job = card.Job;
        if (job.IsActive)
        {
            var stop = MessageBox.Show(
                $"Test na disku {job.Disk.Number} práve beží.\n\nZastaviť a odstrániť?",
                "Kontrola povrchu", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (stop != DialogResult.Yes) return;
            job.Stop();
            await job.WaitAsync(5000);
        }
        RemoveJob(job);
    }

    private void RemoveJob(SurfaceJob job)
    {
        var card = _cards.FirstOrDefault(c => c.Job == job);
        if (card is null) return;
        if (_expanded == card) _expanded = null;
        _cards.Remove(card);
        _jobs.Remove(job);
        _jobsHost.Controls.Remove(card);
        card.Dispose();
        _empty.Visible = _cards.Count == 0;
        ResizeCards();
    }

    private void StopAll()
    {
        foreach (var job in _jobs)
            if (job.IsActive) job.Stop();
    }

    private void ClearCompleted()
    {
        var completed = _cards.Where(c => !c.Job.IsActive).ToList();
        if (completed.Count == 0)
        {
            MessageBox.Show("Žiadne dokončené testy na vyčistenie.", "Kontrola povrchu",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var answer = MessageBox.Show(
            $"Odstrániť {completed.Count} dokončených testov z obrazovky?",
            "Kontrola povrchu", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        foreach (var card in completed) RemoveJob(card.Job);
    }

    private async Task RepairJobAsync(SurfaceJobCard card)
    {
        await card.Job.RepairAsync(FindForm());
        card.RefreshFromJob();
    }

    private int CardWidth()
        => Math.Max(480, _jobsHost.ClientSize.Width -
                         SystemInformation.VerticalScrollBarWidth - 6);

    private void ResizeCards()
    {
        int width = CardWidth();
        _empty.Width = width;
        foreach (var card in _cards) card.Width = width;
    }
}
