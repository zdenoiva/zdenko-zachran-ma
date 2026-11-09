using NtfsRescue.Core;
using NtfsRescue.Repair;
using NtfsRescue.Util;

namespace NtfsRescue.Ui;

public sealed class SurfacePanel : UserControl
{
    private readonly ComboBox _diskCombo = new();
    private readonly Button _btnReload = new();
    private readonly Button _btnScan = new();
    private readonly Button _btnStop = new();
    private readonly Button _btnSave = new();
    private readonly Button _btnRepair = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _status = new();
    private readonly Label _stats = new();
    private readonly Label _info = new();
    private readonly Label _blocked = new();
    private readonly Label _warning = new();
    private readonly SurfaceMapControl _map = new();
    private readonly RichTextBox _log = new();

    private List<DiskInfo> _disks = new();
    private DiskInfo? _selected;
    private SurfaceScanResult? _result;
    private CancellationTokenSource? _cts;
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
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            BackColor = Theme.Background,
            Margin = new Padding(0, 0, 0, 10)
        };
        panel.Controls.Add(new Label
        {
            Text = "Disk:",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.5f),
            ForeColor = Theme.Text,
            Margin = new Padding(0, 8, 8, 0)
        });

        _diskCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _diskCombo.Width = 510;
        _diskCombo.Margin = new Padding(0, 4, 8, 0);
        _diskCombo.SelectedIndexChanged += (_, _) => SelectDisk();
        panel.Controls.Add(_diskCombo);

        _btnReload.Text = "Obnoviť disky";
        _btnReload.AutoSize = true;
        _btnReload.Margin = new Padding(0, 3, 8, 0);
        _btnReload.Click += async (_, _) => await LoadDisksAsync();
        panel.Controls.Add(_btnReload);

        _btnScan.Text = "Spustiť kontrolu povrchu";
        _btnScan.AutoSize = true;
        _btnScan.Font = new Font(Font, FontStyle.Bold);
        _btnScan.Margin = new Padding(0, 3, 8, 0);
        _btnScan.Click += async (_, _) => await ScanAsync();
        panel.Controls.Add(_btnScan);

        _btnStop.Text = "Zastaviť";
        _btnStop.AutoSize = true;
        _btnStop.Visible = false;
        _btnStop.Margin = new Padding(0, 3, 8, 0);
        _btnStop.Click += (_, _) => _cts?.Cancel();
        panel.Controls.Add(_btnStop);

        _btnSave.Text = "Uložiť report";
        _btnSave.AutoSize = true;
        _btnSave.Enabled = false;
        _btnSave.Margin = new Padding(0, 3, 8, 0);
        _btnSave.Click += (_, _) => SaveReport();
        panel.Controls.Add(_btnSave);

        _btnRepair.Text = "Opraviť vadné sektory…";
        _btnRepair.AutoSize = true;
        _btnRepair.Enabled = false;
        _btnRepair.ForeColor = Color.White;
        _btnRepair.BackColor = Theme.Danger;
        _btnRepair.FlatStyle = FlatStyle.Flat;
        _btnRepair.FlatAppearance.BorderSize = 0;
        _btnRepair.Margin = new Padding(0, 3, 0, 0);
        _btnRepair.Click += async (_, _) => await RepairAsync();
        panel.Controls.Add(_btnRepair);
        return panel;
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
            Padding = new Padding(14),
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

        var top = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Surface, Margin = new Padding(0) };
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Size = new Size(360, 16);
        _progress.Margin = new Padding(0, 4, 12, 0);
        top.Controls.Add(_progress);
        _status.Text = "Pripravené";
        _status.AutoSize = true;
        _status.ForeColor = Theme.Muted;
        _status.Margin = new Padding(0, 5, 0, 0);
        top.Controls.Add(_status);
        layout.Controls.Add(top);

        _stats.Text = "Bez kontroly.";
        _stats.AutoSize = true;
        _stats.MaximumSize = new Size(1100, 0);
        _stats.Font = Theme.Body;
        _stats.ForeColor = Theme.Text;
        _stats.Margin = new Padding(0, 8, 0, 0);
        layout.Controls.Add(_stats);

        var legend = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Surface, Margin = new Padding(0, 8, 0, 0) };
        legend.Controls.Add(Legend("OK", Color.FromArgb(26, 166, 108)));
        legend.Controls.Add(Legend("50+ ms", Color.FromArgb(122, 190, 72)));
        legend.Controls.Add(Legend("100+ ms", Color.FromArgb(226, 187, 42)));
        legend.Controls.Add(Legend("250+ ms", Color.FromArgb(242, 130, 28)));
        legend.Controls.Add(Legend("chyba", Color.FromArgb(205, 35, 35)));
        layout.Controls.Add(legend);

        card.Controls.Add(layout);
        return card;
    }

    private static Control Legend(string text, Color color)
    {
        var panel = new Panel { Size = new Size(12, 12), BackColor = color, Margin = new Padding(0, 3, 4, 0) };
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            ForeColor = Theme.Muted,
            Font = Theme.Small,
            Margin = new Padding(0, 0, 12, 0)
        };
        var flow = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Surface, Margin = new Padding(0) };
        flow.Controls.Add(panel);
        flow.Controls.Add(label);
        return flow;
    }

    private Control BuildContent()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 300
        };

        var mapCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            Padding = new Padding(8),
            Margin = new Padding(0, 0, 0, 8)
        };
        mapCard.Paint += PaintBorder;
        _map.Dock = DockStyle.Fill;
        mapCard.Controls.Add(_map);
        split.Panel1.Controls.Add(mapCard);
        split.Panel1.Padding = new Padding(0, 0, 0, 8);

        var logCard = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Surface,
            Padding = new Padding(10)
        };
        logCard.Paint += PaintBorder;
        _log.Dock = DockStyle.Fill;
        _log.ReadOnly = true;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Theme.Surface;
        _log.Font = Theme.Mono;
        _log.WordWrap = true;
        logCard.Controls.Add(_log);
        split.Panel2.Controls.Add(logCard);
        split.Panel2.Padding = new Padding(0, 8, 0, 0);
        return split;
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
        _result = null;
        _map.Reset(0);
        _btnSave.Enabled = false;
        _btnRepair.Enabled = false;
        _stats.Text = "Bez kontroly.";
        if (_selected is null)
        {
            _info.Text = "Vyberte disk.";
            _blocked.Visible = false;
            _btnScan.Enabled = false;
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
        _btnScan.Enabled = !_busy;
        _btnRepair.Enabled = false;
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

        SetBusy(true, "Spúšťam kontrolu povrchu…");
        _result = null;
        _log.Clear();
        _btnSave.Enabled = false;
        _btnRepair.Enabled = false;
        var blocks = (int)Math.Min(int.MaxValue, (disk.Size + SurfaceScanner.BlockSize - 1) / SurfaceScanner.BlockSize);
        _map.Reset(blocks);
        _progress.Value = 0;

        try
        {
            _cts = new CancellationTokenSource();
            var progress = new Progress<SurfaceProgress>(p =>
            {
                var percent = p.TotalBytes > 0 ? (int)(p.DoneBytes * 100 / p.TotalBytes) : 0;
                _progress.Value = Math.Clamp(percent, 0, 100);
                _status.Text = $"Kontrola: {percent} % · {Fmt.Bytes((long)p.BytesPerSecond)}/s · ETA {p.Eta:hh\\:mm\\:ss}";
                _stats.Text = $"{Fmt.Bytes(p.DoneBytes)} / {Fmt.Bytes(p.TotalBytes)} · " +
                              $"{p.BlocksDone:N0} / {p.TotalBlocks:N0} blokov · " +
                              $"uplynulo {p.Elapsed:hh\\:mm\\:ss}";
            });

            var result = await Task.Run(() => SurfaceScanner.Scan(disk, progress, AppendLog,
                _cts.Token, (index, block) =>
                {
                    if (index % 64 != 0) return;
                    try
                    {
                        BeginInvoke(new Action(() =>
                        {
                            _map.SetBlock(index, block);
                            _map.Invalidate();
                        }));
                    }
                    catch { }
                }), _cts.Token);

            _result = result;
            _map.SetBlocks(result.Blocks);
            _progress.Value = 100;
            _stats.Text = result.Summary;
            _status.Text = result.Summary;
            _status.ForeColor = result.BadSectors.Count > 0 ? Theme.Danger : Theme.Success;
            _btnSave.Enabled = true;
            var appDisk = VolumeMapper.GetDiskNumberForPath(Environment.ProcessPath ?? "");
            _btnRepair.Enabled = result.BadSectors.Count > 0 &&
                                 !disk.IsCurrentSystem &&
                                 (appDisk < 0 || appDisk != disk.Number);
            Log.Info(result.Summary);
        }
        catch (OperationCanceledException)
        {
            AppendLog("Kontrola bola zrušená.");
            _status.Text = "Kontrola bola zrušená.";
            _status.ForeColor = Theme.Warning;
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

    private async Task RepairAsync()
    {
        if (_busy || _result is null || _selected is null || _result.BadSectors.Count == 0) return;
        var disk = _selected;
        var text =
            $"Disk {disk.Number} · {disk.Model} · {Fmt.Bytes(disk.Size)}\n\n" +
            $"Vadných sektorov: {_result.BadSectors.Count:N0}\n\n" +
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
                _status.Text = $"{p.Phase}: {p.Attempted:N0}/{p.Total:N0} · opravené {p.Repaired:N0} · chybné {p.Failed:N0}";
            });

            var result = await Task.Run(() => SurfaceRepairService.RepairAsync(disk,
                _result.BadSectors, progress, AppendLog, _cts.Token), _cts.Token);
            _stats.Text = result.Summary + (string.IsNullOrWhiteSpace(result.BackupPath)
                ? "" : $" Záloha: {result.BackupPath}");
            _status.Text = result.Summary;
            _status.ForeColor = result.Failed == 0 ? Theme.Success : Theme.Warning;
            _btnRepair.Enabled = false;
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
        if (_result is null) return;
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV report|*.csv|Všetky súbory|*.*",
            FileName = $"povrch-disk-{_selected?.Number}-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            Title = "Uložiť report kontroly povrchu"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            _result.SaveReport(dialog.FileName);
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

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        _diskCombo.Enabled = !busy;
        _btnReload.Enabled = !busy;
        _btnScan.Enabled = !busy && _selected is not null;
        _btnStop.Visible = busy;
        _btnSave.Enabled = !busy && _result is not null;
        _btnRepair.Enabled = !busy && _result is not null &&
                             _result.BadSectors.Count > 0 &&
                             _selected is not null && !_selected.IsCurrentSystem &&
                             (VolumeMapper.GetDiskNumberForPath(Environment.ProcessPath ?? "") != _selected.Number);
        if (status is not null)
        {
            _status.Text = status;
            _status.ForeColor = Theme.Muted;
        }
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
