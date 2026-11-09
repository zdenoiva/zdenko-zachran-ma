using System.Globalization;
using System.Text;
using NtfsRescue.Core;
using NtfsRescue.Repair;
using NtfsRescue.Util;

namespace NtfsRescue.Ui;

public sealed class SurfacePanel : UserControl
{
    private const int TargetMapCells = 2048;

    private readonly ComboBox _diskCombo = new();
    private readonly NumericUpDown _startBlock = new();
    private readonly NumericUpDown _endBlock = new();
    private readonly Button _btnReload = new();
    private readonly Button _btnScan = new();
    private readonly Button _btnStop = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnRepair = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _status = new();
    private readonly Label _percent = new();
    private readonly Label _info = new();
    private readonly Label _blocked = new();
    private readonly Label _warning = new();
    private readonly Label _cellSize = new();
    private readonly Label _rangeHint = new();
    private readonly Label _valDamaged = new();
    private readonly Label _capDamaged = new();
    private readonly Label _valSpeed = new();
    private readonly Label _valPosition = new();
    private readonly Label _capPosition = new();
    private readonly Label _valTime = new();
    private readonly Label _capTime = new();
    private readonly SurfaceMapControl _map = new();
    private readonly RichTextBox _log = new();

    private readonly object _mapLock = new();
    private List<DiskInfo> _disks = new();
    private DiskInfo? _selected;
    private CancellationTokenSource? _cts;
    private int[] _cells = Array.Empty<int>();
    private double[] _cellMs = Array.Empty<double>();
    private readonly HashSet<long> _badSectors = new();
    private long _cellBytes = SurfaceScanner.BlockSize;
    private int _blocksPerCell = 1;
    private bool _hasScan;
    private bool _busy;
    private bool _loaded;

    public SurfacePanel()
    {
        Dock = DockStyle.Fill;
        BackColor = Theme.Background;
        BuildUi();
    }

    public async Task EnsureLoadedAsync()
    {
        if (!_loaded) await LoadDisksAsync();
    }

    public bool SelectDiskNumber(int number)
    {
        for (int i = 0; i < _diskCombo.Items.Count; i++)
        {
            if (_diskCombo.Items[i] is DiskInfo disk && disk.Number == number)
            {
                if (_diskCombo.SelectedIndex == i) SelectDisk();
                else _diskCombo.SelectedIndex = i;
                return true;
            }
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
            RowCount = 4,
            Padding = new Padding(18, 14, 18, 14),
            BackColor = Theme.Background
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildToolbar(), 0, 0);
        root.Controls.Add(BuildInfo(), 0, 1);
        root.Controls.Add(BuildStats(), 0, 2);
        root.Controls.Add(BuildContent(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildToolbar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 3,
            RowCount = 2,
            BackColor = Theme.Background,
            Margin = new Padding(0, 0, 0, 10)
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
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
        bar.SetColumnSpan(diskRow, 2);

        var diskButtons = new FlowLayoutPanel
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
        diskButtons.Controls.Add(_btnReload);

        _btnScan.Text = "Spustiť kontrolu povrchu";
        StylePrimary(_btnScan);
        _btnScan.Click += async (_, _) => await ScanAsync();
        diskButtons.Controls.Add(_btnScan);

        _btnStop.Text = "Zastaviť";
        StyleSecondary(_btnStop);
        _btnStop.Visible = false;
        _btnStop.Click += (_, _) => _cts?.Cancel();
        diskButtons.Controls.Add(_btnStop);
        bar.Controls.Add(diskButtons, 2, 0);

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

        var actionRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            BackColor = Theme.Background,
            Margin = new Padding(0, 8, 0, 0)
        };
        _btnSave.Text = "Uložiť report";
        StyleSecondary(_btnSave);
        _btnSave.Enabled = false;
        _btnSave.Click += (_, _) => SaveReport();
        actionRow.Controls.Add(_btnSave);

        _btnRepair.Text = "Opraviť vadné sektory…";
        _btnRepair.AutoSize = true;
        _btnRepair.Height = 34;
        SetRepairEnabled(false);
        _btnRepair.ForeColor = Color.White;
        _btnRepair.BackColor = Theme.Danger;
        _btnRepair.FlatStyle = FlatStyle.Flat;
        _btnRepair.FlatAppearance.BorderSize = 0;
        _btnRepair.FlatAppearance.MouseOverBackColor = Color.FromArgb(160, 45, 45);
        _btnRepair.Font = new Font("Segoe UI Semibold", 9.5f);
        _btnRepair.Cursor = Cursors.Hand;
        _btnRepair.Padding = new Padding(14, 0, 14, 0);
        _btnRepair.Margin = new Padding(10, 2, 0, 0);
        _btnRepair.Click += async (_, _) => await RepairAsync();
        actionRow.Controls.Add(_btnRepair);
        bar.Controls.Add(actionRow, 2, 1);

        return bar;
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
        numeric.Width = 104;
        numeric.Minimum = 0;
        numeric.Maximum = 0;
        numeric.ThousandsSeparator = true;
        numeric.Font = Theme.Body;
        numeric.Margin = new Padding(0, 5, 0, 0);
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
        card.Paint += PaintBorder;

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
        _info.MaximumSize = new Size(960, 0);
        _info.Font = Theme.Body;
        _info.ForeColor = Theme.Text;
        layout.Controls.Add(_info);

        _blocked.AutoSize = true;
        _blocked.MaximumSize = new Size(960, 0);
        _blocked.ForeColor = Theme.Danger;
        _blocked.Font = new Font("Segoe UI Semibold", 9f);
        _blocked.Visible = false;
        _blocked.Margin = new Padding(0, 6, 0, 0);
        layout.Controls.Add(_blocked);

        _warning.AutoSize = true;
        _warning.MaximumSize = new Size(960, 0);
        _warning.ForeColor = Theme.Warning;
        _warning.Font = Theme.Small;
        _warning.Margin = new Padding(0, 6, 0, 0);
        _warning.Text =
            "Kontrola povrchu je len čítanie a môže trvať desiatky minút až hodiny. " +
            "Oprava zapisuje do vadných sektorov — dáta v nich môžu byť nenávratne zničené. " +
            "Pri SSD nemusí zápis zabezpečiť fyzické vymazanie; použite výrobcom doporučený secure erase.";
        layout.Controls.Add(_warning);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildStats()
    {
        var card = new Panel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = Theme.Surface,
            Padding = new Padding(14, 12, 14, 10),
            Margin = new Padding(0, 0, 0, 10)
        };
        card.Paint += PaintBorder;

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Surface
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var progressRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Size = new Size(420, 18);
        _progress.Margin = new Padding(0, 5, 14, 0);
        progressRow.Controls.Add(_progress);

        _percent.Text = "0 %";
        _percent.AutoSize = true;
        _percent.Font = new Font("Segoe UI Semibold", 12f);
        _percent.ForeColor = Theme.Text;
        _percent.Margin = new Padding(0, 1, 16, 0);
        progressRow.Controls.Add(_percent);

        _status.Text = "Pripravené";
        _status.AutoSize = true;
        _status.ForeColor = Theme.Muted;
        _status.Font = Theme.Body;
        _status.Margin = new Padding(0, 6, 0, 0);
        progressRow.Controls.Add(_status);
        stack.Controls.Add(progressRow, 0, 0);

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 8, 0, 0)
        };
        for (int i = 0; i < 4; i++)
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        metrics.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        metrics.Controls.Add(Metric("Poškodené bloky", _valDamaged, _capDamaged, Theme.Success), 0, 0);
        metrics.Controls.Add(Metric("Rýchlosť", _valSpeed, null, Theme.Text), 1, 0);
        metrics.Controls.Add(Metric("Pozícia", _valPosition, _capPosition, Theme.Text), 2, 0);
        metrics.Controls.Add(Metric("Uplynulý čas", _valTime, _capTime, Theme.Text), 3, 0);
        stack.Controls.Add(metrics, 0, 1);

        var legend = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 8, 0, 0)
        };
        legend.Controls.Add(LegendRow("OK (do 50 ms)", SurfaceMapControl.Ok));
        legend.Controls.Add(LegendRow("50 – 100 ms", SurfaceMapControl.Medium));
        legend.Controls.Add(LegendRow("100 – 250 ms", SurfaceMapControl.Slow));
        legend.Controls.Add(LegendRow("nad 250 ms", SurfaceMapControl.VerySlow));
        legend.Controls.Add(LegendRow("chyba / vadný blok", SurfaceMapControl.Bad));

        _cellSize.AutoSize = true;
        _cellSize.Font = Theme.Small;
        _cellSize.ForeColor = Theme.Muted;
        _cellSize.Margin = new Padding(8, 3, 0, 0);
        _cellSize.Text = "1 štvorec ≈ 4 MB";
        legend.Controls.Add(_cellSize);
        stack.Controls.Add(legend, 0, 2);

        card.Controls.Add(stack);
        return card;
    }

    private Control BuildContent()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Theme.Background
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var mapCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 10, 0)
        };
        mapCard.Paint += PaintBorder;
        _map.Dock = DockStyle.Fill;
        mapCard.Controls.Add(_map);
        content.Controls.Add(mapCard, 0, 0);
        content.Controls.Add(BuildLogCard(), 1, 0);
        return content;
    }

    private static Control LegendRow(string text, Color color)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 2, 18, 0)
        };
        row.Controls.Add(new Panel
        {
            Size = new Size(14, 14),
            BackColor = color,
            Margin = new Padding(0, 3, 8, 0)
        });
        row.Controls.Add(new Label
        {
            Text = text,
            AutoSize = true,
            Font = Theme.Body,
            ForeColor = Theme.Text,
            Margin = new Padding(0, 1, 0, 0)
        });
        return row;
    }

    private static Control Metric(string caption, Label value, Label? sub, Color valueColor)
    {
        var box = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 12, 0, 0)
        };
        box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        box.Controls.Add(new Label
        {
            Text = caption.ToUpperInvariant(),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 8f),
            ForeColor = Theme.Muted,
            Margin = new Padding(0)
        }, 0, 0);

        value.Text = "—";
        value.AutoSize = true;
        value.Font = new Font("Segoe UI Semibold", 17f);
        value.ForeColor = valueColor;
        value.Margin = new Padding(0, 2, 0, 0);
        box.Controls.Add(value, 0, 1);

        if (sub is not null)
        {
            sub.Text = "";
            sub.AutoSize = true;
            sub.Font = Theme.Small;
            sub.ForeColor = Theme.Muted;
            sub.Margin = new Padding(0, 1, 0, 0);
            box.Controls.Add(sub, 0, 2);
        }
        return box;
    }

    private Control BuildLogCard()
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(0)
        };
        card.Paint += PaintBorder;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = "PROTOKOL KONTROLY",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 8f),
            ForeColor = Theme.Muted,
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);

        _log.Dock = DockStyle.Fill;
        _log.ReadOnly = true;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Theme.Surface;
        _log.Font = Theme.Mono;
        _log.WordWrap = true;
        layout.Controls.Add(_log, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private void PaintBorder(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel panel) return;
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
    }

    private async Task LoadDisksAsync()
    {
        if (_busy) return;
        SetBusy(true, "Načítavam disky…");
        try
        {
            var previous = _selected?.Number;
            _disks = await DiskInfoCatalog.LoadAsync(CancellationToken.None);
            _diskCombo.Items.Clear();
            foreach (var disk in _disks) _diskCombo.Items.Add(disk);
            if (_disks.Count == 0)
            {
                _selected = null;
                _info.Text = "Nenašli sa žiadne fyzické disky.";
                return;
            }

            _selected = previous.HasValue
                ? _disks.FirstOrDefault(d => d.Number == previous.Value) ?? _disks[0]
                : _disks.FirstOrDefault(d => !d.IsCurrentSystem) ?? _disks[0];
            _diskCombo.SelectedItem = _selected;
            SelectDisk();
            _loaded = true;
            _status.Text = "Pripravené";
            _status.ForeColor = Theme.Muted;
        }
        catch (Exception ex)
        {
            AppendLog("Načítanie diskov zlyhalo: " + ex.Message);
            _status.Text = "Disky sa nepodarilo načítať.";
            _status.ForeColor = Theme.Danger;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SelectDisk()
    {
        _selected = _diskCombo.SelectedItem as DiskInfo;
        _hasScan = false;
        _badSectors.Clear();
        ResetStats();

        if (_selected is null)
        {
            _info.Text = "Vyberte disk.";
            _blocked.Visible = false;
            _btnScan.Enabled = false;
            _startBlock.Maximum = 0;
            _endBlock.Maximum = 0;
            _rangeHint.Text = "";
            _cells = Array.Empty<int>();
            _cellMs = Array.Empty<double>();
            _map.SetCells(null);
            _cellSize.Text = "";
            return;
        }

        var d = _selected;
        _info.Text =
            $"Disk {d.Number} · {d.Model} · {Fmt.Bytes(d.Size)} · " +
            $"{(string.IsNullOrWhiteSpace(d.PartitionStyle) ? "?" : d.PartitionStyle)} · {d.BusType}" +
            (string.IsNullOrWhiteSpace(d.Serial) ? "" : " · S/N " + d.Serial);

        var appDisk = VolumeMapper.GetDiskNumberForPath(Environment.ProcessPath ?? "");
        string blocked = "";
        if (d.IsCurrentSystem)
            blocked = "Aktuálny systémový disk: kontrola povrchu je povolená len na čítanie; oprava zápisom je zablokovaná.";
        else if (appDisk >= 0 && appDisk == d.Number)
            blocked = "Program beží z tohto disku. Oprava zápisom je zablokovaná.";
        _blocked.Text = blocked;
        _blocked.Visible = blocked.Length > 0;

        PrepareMap(d);
        _btnScan.Enabled = !_busy;
        SetRepairEnabled(false);
        _status.Text = "Pripravené";
        _status.ForeColor = Theme.Muted;
        _progress.Value = 0;
        _percent.Text = "0 %";
        _map.Invalidate();
    }

    private void PrepareMap(DiskInfo disk)
    {
        int fullBlocks = SurfaceScanner.BlockCount(disk.Size);
        _blocksPerCell = Math.Max(1, (int)Math.Ceiling((double)fullBlocks / TargetMapCells));
        int cellCount = Math.Max(1, (int)Math.Ceiling((double)fullBlocks / _blocksPerCell));
        _cellBytes = (long)_blocksPerCell * SurfaceScanner.BlockSize;
        _cells = new int[cellCount];
        _cellMs = new double[cellCount];
        _map.SetCells(_cells);
        _cellSize.Text = "1 štvorec ≈ " + Fmt.Bytes(_cellBytes);

        _startBlock.Maximum = Math.Max(0, fullBlocks - 1);
        _endBlock.Maximum = Math.Max(0, fullBlocks - 1);
        _startBlock.Value = 0;
        _endBlock.Value = _endBlock.Maximum;
        _rangeHint.Text = $"z {fullBlocks:N0} blokov · blok = {Fmt.Bytes(SurfaceScanner.BlockSize)}";
    }

    private void ResetStats()
    {
        SetDamaged(0, 0, 0);
        _valSpeed.Text = "—";
        _valPosition.Text = "—";
        _capPosition.Text = "";
        _valTime.Text = "00:00:00";
        _capTime.Text = "";
    }

    private void SetDamaged(int badCells, int knownCells, int badSectors)
    {
        double percent = knownCells > 0 ? (double)badCells * 100 / knownCells : 0;
        _valDamaged.Text = percent.ToString("0.0", CultureInfo.CurrentCulture) + " %";
        _valDamaged.ForeColor = badCells > 0 ? Theme.Danger : Theme.Success;
        _capDamaged.Text = $"{badCells:N0} blokov · {badSectors:N0} sektorov";
    }

    private async Task ScanAsync()
    {
        if (_busy || _selected is null) return;
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

        if (first == 0)
        {
            lock (_mapLock)
            {
                Array.Clear(_cells);
                Array.Clear(_cellMs);
            }
            _badSectors.Clear();
        }

        SetBusy(true, "Spúšťam kontrolu povrchu…");
        _log.Clear();
        _hasScan = true;
        _progress.Value = 0;
        _percent.Text = "0 %";
        ResetStats();
        _map.Invalidate();

        try
        {
            _cts = new CancellationTokenSource();
            var progress = new Progress<SurfaceProgress>(p => UpdateProgress(disk, p));
            var result = await Task.Run(() => SurfaceScanner.Scan(disk, first, last, progress,
                AppendLog, _cts.Token, OnBlock, OnBadSector), _cts.Token);

            foreach (var sector in result.BadSectors) _badSectors.Add(sector);
            _progress.Value = 100;
            _percent.Text = "100 %";
            _status.Text = result.Summary;
            _status.ForeColor = result.BadSectors.Count > 0 ? Theme.Danger : Theme.Success;
            _valSpeed.Text = result.Duration.TotalSeconds > 0
                ? Fmt.Bytes((long)(result.TotalBytes / result.Duration.TotalSeconds)) + "/s"
                : "—";
            _valTime.Text = FormatDuration(result.Duration);
            _capTime.Text = "dokončené";
            _valPosition.Text = Fmt.Bytes(Math.Min(disk.Size, (long)(result.LastBlock + 1) * SurfaceScanner.BlockSize));
            _capPosition.Text = "koniec rozsahu";
            UpdateDamaged(disk);
            _map.Invalidate();
            Log.Info(result.Summary);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Kontrola bola zrušená. Namereané výsledky zostávajú v mape.");
            _status.Text = "Kontrola bola zrušená.";
            _status.ForeColor = Theme.Warning;
            UpdateDamaged(disk);
        }
        catch (Exception ex)
        {
            AppendLog("Kontrola zlyhala: " + ex.Message);
            _status.Text = "Kontrola zlyhala.";
            _status.ForeColor = Theme.Danger;
            MessageBox.Show("Kontrola povrchu zlyhala:\n\n" + ex.Message, "Kontrola povrchu",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            SetBusy(false);
        }
    }

    private void OnBlock(int index, SurfaceBlock block)
    {
        int cell = index / _blocksPerCell;
        int severity = SurfaceScanner.Severity(block);
        lock (_mapLock)
        {
            if (cell < 0 || cell >= _cells.Length) return;
            if (severity > _cells[cell]) _cells[cell] = severity;
            if (block.Ms > _cellMs[cell]) _cellMs[cell] = block.Ms;
        }
    }

    private void OnBadSector(long offset)
    {
        lock (_mapLock) _badSectors.Add(offset);
    }

    private void UpdateProgress(DiskInfo disk, SurfaceProgress p)
    {
        int percent = p.TotalBytes > 0 ? (int)(p.DoneBytes * 100 / p.TotalBytes) : 0;
        _progress.Value = Math.Clamp(percent, 0, 100);
        _percent.Text = percent + " %";
        _status.Text = $"Kontrola · {p.BlocksDone:N0} / {p.TotalBlocks:N0} blokov · ETA {FormatDuration(p.Eta)}";
        _status.ForeColor = Theme.Muted;
        _valSpeed.Text = Fmt.Bytes((long)p.BytesPerSecond) + "/s";
        _valPosition.Text = Fmt.Bytes(p.Position);
        _capPosition.Text = Math.Min(100, p.Position * 100 / Math.Max(1, disk.Size)) + " % disku";
        _valTime.Text = FormatDuration(p.Elapsed);
        _capTime.Text = "zostáva " + FormatDuration(p.Eta);
        UpdateDamaged(disk);
        _map.Invalidate();
    }

    private void UpdateDamaged(DiskInfo disk)
    {
        int badCells = 0;
        int knownCells = 0;
        int badSectors;
        lock (_mapLock)
        {
            foreach (var state in _cells)
            {
                if (state > 0) knownCells++;
                if (state >= 5) badCells++;
            }
            badSectors = _badSectors.Count;
        }
        SetDamaged(badCells, knownCells, badSectors);
    }

    private static string FormatDuration(TimeSpan value)
        => value.TotalHours >= 1
            ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";

    private async Task RepairAsync()
    {
        if (_busy || _selected is null) return;
        var disk = _selected;
        List<long> sectors;
        lock (_mapLock) sectors = _badSectors.OrderBy(x => x).ToList();
        if (sectors.Count == 0) return;

        var text =
            $"Disk {disk.Number} · {disk.Model} · {Fmt.Bytes(disk.Size)}\n\n" +
            $"Vadných sektorov: {sectors.Count:N0}\n\n" +
            "Program sa pokúsi znovu prečítať každý vadný sektor, uloží čitateľné dáta " +
            "do záložného súboru a zapíše ich späť. Ak sa sektor nedá prečítať, zapíše nuly " +
            "a pokúsi sa vynútiť preradenie sektora diskom.\n\n" +
            "POZOR: Dáta v poškodených sektoroch môžu byť nenávratne stratené. " +
            "Pri SSD nemusí zápis vynútiť fyzické preradenie.\n\nPokračovať?";

        using var confirm = new ConfirmForm("Oprava vadných sektorov", text, "OPRAVIŤ",
            "Rozumiem, že oprava zapisuje do disku a môže poškodiť dáta.");
        if (confirm.ShowDialog(FindForm()) != DialogResult.OK) return;

        SetBusy(true, "Opravujem vadné sektory…");
        _log.Clear();
        try
        {
            _cts = new CancellationTokenSource();
            var progress = new Progress<SurfaceRepairProgress>(p =>
            {
                _progress.Value = p.Total > 0 ? Math.Clamp(p.Attempted * 100 / p.Total, 0, 100) : 0;
                _percent.Text = _progress.Value + " %";
                _status.Text = $"{p.Phase}: {p.Attempted:N0}/{p.Total:N0} · opravené {p.Repaired:N0} · chybné {p.Failed:N0}";
            });

            var result = await Task.Run(() => SurfaceRepairService.RepairAsync(disk,
                sectors, progress, AppendLog, _cts.Token), _cts.Token);
            _status.Text = result.Summary;
            _status.ForeColor = result.Failed == 0 ? Theme.Success : Theme.Warning;
            SetRepairEnabled(false);
            MessageBox.Show(result.Summary + (string.IsNullOrWhiteSpace(result.BackupPath)
                    ? "" : "\n\nZáloha čitateľných dát:\n" + result.BackupPath) +
                "\n\nSpustite kontrolu povrchu znova a overte stav.",
                "Oprava vadných sektorov", MessageBoxButtons.OK,
                result.Failed == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Oprava bola zrušená.");
        }
        catch (Exception ex)
        {
            AppendLog("Oprava zlyhala: " + ex.Message);
            MessageBox.Show("Oprava zlyhala:\n\n" + ex.Message, "Oprava vadných sektorov",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            SetBusy(false);
        }
    }

    private void SaveReport()
    {
        if (!_hasScan || _selected is null) return;
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV report|*.csv|Všetky súbory|*.*",
            FileName = $"povrch-disk-{_selected.Number}-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            Title = "Uložiť report kontroly povrchu"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            SaveSessionReport(dialog.FileName, _selected);
            AppendLog("Report uložený: " + dialog.FileName);
            MessageBox.Show("Report bol uložený:\n" + dialog.FileName, "Kontrola povrchu",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Report sa nepodarilo uložiť:\n" + ex.Message, "Kontrola povrchu",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveSessionReport(string path, DiskInfo disk)
    {
        int[] cells;
        double[] ms;
        List<long> sectors;
        lock (_mapLock)
        {
            cells = (int[])_cells.Clone();
            ms = (double[])_cellMs.Clone();
            sectors = _badSectors.OrderBy(x => x).ToList();
        }

        var sb = new StringBuilder();
        sb.AppendLine("Zdenko, zachran ma! - kontrola povrchu");
        sb.AppendLine($"disk;{disk.Number};{disk.Model};{disk.Size};{_cellBytes}");
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
            long offset = i * _cellBytes;
            sb.AppendLine($"{offset},{_cellBytes}," +
                          $"{ms[i].ToString("F1", CultureInfo.InvariantCulture)},{status}");
        }
        sb.AppendLine();
        sb.AppendLine("# vadne sektory");
        foreach (var offset in sectors)
            sb.AppendLine(offset.ToString(CultureInfo.InvariantCulture));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        _diskCombo.Enabled = !busy;
        _startBlock.Enabled = !busy;
        _endBlock.Enabled = !busy;
        _btnReload.Enabled = !busy;
        _btnScan.Enabled = !busy && _selected is not null;
        _btnStop.Visible = busy;
        _btnSave.Enabled = !busy && _hasScan;
        SetRepairEnabled(!busy && _hasScan && _badSectors.Count > 0 &&
                         _selected is not null && !_selected.IsCurrentSystem &&
                         (VolumeMapper.GetDiskNumberForPath(Environment.ProcessPath ?? "") != _selected.Number));
        if (status is not null)
        {
            _status.Text = status;
            _status.ForeColor = Theme.Muted;
        }
    }

    private void SetRepairEnabled(bool enabled)
    {
        _btnRepair.Enabled = enabled;
        _btnRepair.BackColor = enabled ? Theme.Danger : Color.FromArgb(219, 224, 231);
        _btnRepair.ForeColor = enabled ? Color.White : Theme.Muted;
    }

    private void AppendLog(string line)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action<string>(AppendLog), line); } catch { }
            return;
        }
        _log.AppendText(line + Environment.NewLine);
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
        Log.Write("Povrch: " + line);
    }
}


